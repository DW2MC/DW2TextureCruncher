using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using Xenko.Core;
using Xenko.Core.IO;
using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;
using Xenko.Core.Storage;
using Xenko.Core.Streaming;
using Xenko.Graphics;

class TextureCruncher
{
    public void CrunchTextures()
    {
        Console.WriteLine("Reading DW2 asset bundles...");
        foreach (var provider in VirtualFileSystem.Providers)
        {
            if(provider is FileSystemProvider fs)
            {
                fs.ChangeBasePath(Test.DW2Path + "\\data\\");
            }
        }        
        
        var bundles = Directory.EnumerateFiles("data\\db\\bundles", "*.bundle").Select(f => (Path.GetFileNameWithoutExtension(f))).Distinct();
        
        using (ObjectDatabase odb = ObjectDatabase.CreateDefaultDatabase())
        {
            foreach (var bundle in bundles)
            {
                odb.LoadBundle(bundle);
                
            }
            TotalItems = odb.ContentIndexMap.GetMergedIdMap().GroupBy(x=>x.Key).Select(group => group.First()).Count();
           
            Dictionary<PixelFormat, int> pixelFormats = new Dictionary<PixelFormat, int>();

            Console.WriteLine("Beginning texture compression...");
            foreach (var kvp in odb.ContentIndexMap.GetMergedIdMap())
            {
                ProcessedItems++;
                PrintProgress();

                var name = kvp.Key;
                var object_id = kvp.Value;
                if (odb.Exists(object_id))
                {
                    DatabaseFileProvider fileProvider = new DatabaseFileProvider(odb);
                    //using (Stream s = odb.Read(object_id))
                    using (Stream s = fileProvider.OpenStream(name, VirtualFileMode.Open, VirtualFileAccess.Read, VirtualFileShare.Read, StreamFlags.Seekable))
                    {
                        BinarySerializationReader ss = new BinarySerializationReader(s);
                        ChunkHeader chunkHeader = ChunkHeader.Read(ss);

                        if (chunkHeader == null)
                            continue;

                        //Fake seek
                        byte[] garbage = new byte[chunkHeader.OffsetToObject - s.Position];
                        s.Read(garbage, 0, chunkHeader.OffsetToObject - (int)s.Position);

                        bool is_streaming_texture = ss.ReadBoolean();
                        if (is_streaming_texture)
                        {
                            continue;
                        }


                        if (chunkHeader.Type.StartsWith("Xenko.Graphics.Texture"))
                        {
                            //int TEXTURE_IDENTIFIER = BitConverter.ToInt32(new byte[] { (byte)'T', (byte)'K', (byte)'T', (byte)'X' },0);
                            using (Image image = Image.Load(s))
                            {
                                if (!pixelFormats.ContainsKey(image.Description.Format))
                                    pixelFormats[image.Description.Format] = 0;
                                pixelFormats[image.Description.Format]++;
                                CompressImage(name, image);
                            }
                        }
                    }
                }              
            }           
         }

        Console.WriteLine("\nDone!");
        var temp_paths = new string[] {
            Path.GetFullPath(Path.Combine(Test.DW2Path, "mods", "Crunched", "Temp")),
            Path.GetFullPath(Path.Combine(Test.DW2Path, "mods", "Crunched", "cache")),
            Path.GetFullPath(Path.Combine(Test.DW2Path, "mods", "Crunched", "local")),
            Path.GetFullPath(Path.Combine(Test.DW2Path, "mods", "Crunched", "roaming"))
        };

        foreach (var temp_path in temp_paths)
        {
            if (Directory.Exists(temp_path))
            {
                Directory.Delete(temp_path, true);
            }
        }
    }

    public int TotalItems { get; set; } = 0;
    public int ProcessedItems { get; set; } = 0;

    private void PrintProgress()
    {
        //int x = Console.CursorLeft;
        //int y = Console.CursorTop;
        //Console.CursorTop = Console.WindowTop + Console.WindowHeight - 1;
        Console.Write("\r{0}/{1} items processed. ({2}%)", ProcessedItems, TotalItems, (int)(((float)ProcessedItems / TotalItems) * 100.0f));
        
        //Console.SetCursorPosition(x, y);
        
    }

    private void CompressImage(string asset_name, Image image)
    {
        if (image.Description.Width % 4 != 0 || image.Description.Height % 4 != 0)
            return;

        /*BLACKLIST*/
        if (asset_name.Contains("rectangle_button_"))
            return;

        var output_path = Path.GetFullPath(Path.Combine(Test.DW2Path, "mods", "Crunched", "Temp", asset_name)) + ".dds";
        var compressed_path = Path.GetFullPath(Path.Combine(Test.DW2Path, "mods", "Crunched", "Temp", "compressed", asset_name)) + ".dds";
        var result_path = Path.GetFullPath(Path.Combine(Test.DW2Path, "mods", "Crunched", "assets", asset_name));

        Directory.CreateDirectory(Directory.GetParent(output_path).FullName);
        Directory.CreateDirectory(Directory.GetParent(compressed_path).FullName);
        Directory.CreateDirectory(Directory.GetParent(result_path).FullName);

        PixelFormat format = image.Description.Format;
        PixelFormat output_format;

        switch (format)
        {
            case PixelFormat.B8G8R8A8_UNorm_SRgb:
            case PixelFormat.R8G8B8A8_UNorm_SRgb:
            case PixelFormat.BC3_UNorm_SRgb:
                output_format = PixelFormat.BC7_UNorm_SRgb;
                break;
            case PixelFormat.R8G8B8A8_UNorm:
            case PixelFormat.B8G8R8A8_UNorm:
                output_format = PixelFormat.BC7_UNorm;
                break;
            default:
                output_format = format;
                break;
        }

        if (output_format != format)
        {
            using (FileStream output_stream = File.OpenWrite(output_path))
            {
                image.Save(output_stream, ImageFileType.Dds);
            }
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "texconv.exe";
            startInfo.Arguments = "-f " + output_format.ToString().ToUpperInvariant() + " -o \"" + Directory.GetParent(compressed_path) + "\" \"" + output_path + "\" ";
            startInfo.WorkingDirectory = Directory.GetParent(Assembly.GetEntryAssembly().Location).FullName;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            var p = Process.Start(startInfo);
            p.WaitForExit();

            File.Delete(output_path);

            using (FileStream compressed_stream = File.OpenRead(compressed_path))
            {
                using (Image compressed_image = Image.Load(compressed_stream))
                {
                    using (FileStream dest_stream = File.OpenWrite(result_path))
                    {
                        BinarySerializationWriter serializationWriter = new BinarySerializationWriter(dest_stream);
                        ChunkHeader header = new ChunkHeader
                        {
                            Type = typeof(Texture).AssemblyQualifiedName
                        };
                        header.Write(serializationWriter);
                        header.OffsetToReferences = (int)serializationWriter.NativeStream.Position;
                        serializationWriter.Write(0);
                        header.OffsetToObject = (int)serializationWriter.NativeStream.Position;
                        serializationWriter.Write<byte>(0);
                        compressed_image.Save(dest_stream, ImageFileType.Xenko);
                        serializationWriter.Write(0);
                        dest_stream.Position = 0;
                        header.Write(serializationWriter);
                    }
                }
            }

            File.Delete(compressed_path);
        }               
    }
}

class Test
{
    public static string DW2Path = string.Empty;
    public static void Main(string[] args)
    {
        

        if (args.Length == 0)
        {
            string[] candidate_paths = {
                "..\\..\\",
                "C:\\Steam\\steamapps\\common\\Distant Worlds 2\\",
                "C:\\Program Files\\Steam\\steamapps\\common\\Distant Worlds 2\\",
                "F:\\Steam\\steamapps\\common\\Distant Worlds 2\\"
            };

            foreach (string candidate in candidate_paths)
            {
                if (File.Exists(Path.Combine(candidate, "DistantWorlds2.exe")))
                {
                    DW2Path = Path.GetFullPath(candidate);
                    break;
                }
            }

            if (DW2Path == string.Empty)
            {
                Console.WriteLine("Distant Worlds 2 path not specified. Please place the executable in the \"Distant Worlds 2\\mods\\Crunched\" folder.");
                Console.WriteLine("Or pass the path to Distant Worlds 2 as an argument.");
                Console.WriteLine("Usage: \"dw2crunch [DW2Path]\" ");
                Console.WriteLine("Example: \"dw2crunch C:\\Steam\\steamapps\\common\\Distant Worlds 2\\\"");
                return;
            }
        }
        else
        {
            DW2Path = args[0];
        }

        Directory.SetCurrentDirectory(DW2Path);
        
        if(!File.Exists("DistantWorlds2.ModLoader.dll"))
        {
            Console.WriteLine("DW2 ModLoader not found.");
            Console.WriteLine("Please go to https://github.com/DW2MC/DW2ModLoader/releases and install the latest version.");
        }

        AppDomain currentDomain = AppDomain.CurrentDomain;
        currentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

        TextureCruncher cruncher = new TextureCruncher();
        cruncher.CrunchTextures();
    }

    private static System.Reflection.Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
        AssemblyName assemblyName = new AssemblyName(args.Name);
        DirectoryInfo directoryInfo = new DirectoryInfo(DW2Path);

        foreach (FileInfo fileInfo in directoryInfo.GetFiles())
        {
            string fileNameWithoutExt = fileInfo.Name.Replace(fileInfo.Extension, "");

            if (assemblyName.Name.ToUpperInvariant() == fileNameWithoutExt.ToUpperInvariant())
            {
                var assembly = Assembly.LoadFrom(fileInfo.FullName);
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    Assembly.Load(reference);
                }
                return assembly;
            }
        }

        return null;
    }
}