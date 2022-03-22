using DistantWorlds.Types;
using DistantWorlds2;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Xenko.Engine;
using Xenko.Graphics;
using Xenko.Rendering;

namespace CompressedTextureLoader
{
    public class Mod
    {
        public Mod(DWGame game)
        {
            Harmony.DEBUG = true;
            new Harmony(nameof(CompressedTextureLoader)).PatchAll();
        }        
    }


    [HarmonyPatch]
    public class GenerateTextureArrayPatch
    {
        public static MethodBase TargetMethod() => typeof(DistantWorlds.Types.DrawingHelper).GetMethod(nameof(DistantWorlds.Types.DrawingHelper.GenerateTextureArray));

        public static bool Prefix(GraphicsDevice graphics,
              CommandList commandList,
              List<Texture> textures,
              int width,
              int height,
              bool useMipMaps,
              int mipCount,
              bool isCompressed,
              ref Texture __result)
        {
            __result = null;

            if (textures.Count == 0)
                return false;

            //Assumes all textures are same format.
            PixelFormat format = textures[0].Format;
            Texture textureArray = Texture.New2D(graphics, width, height, mipCount, format, arraySize: textures.Count);
            int dest_subresource = 0;
            for (int i = 0; i < textures.Count; i++)
            {
                for (int j = 0; j < mipCount; j++)
                {
                    commandList.CopyRegion(textures[i], j, null, textureArray, dest_subresource);
                    dest_subresource++;
                }
            }
            __result = textureArray;

            return false;
        }
    }

    [HarmonyPatch]
    public class  GenerateTextureWithPreGeneratedMipmapsPatch
    {
        public static MethodBase TargetMethod() => typeof(DistantWorlds.Types.DrawingHelper).GetMethod(nameof(DistantWorlds.Types.DrawingHelper.GenerateTextureWithPreGeneratedMipMaps));

        public static bool Prefix(GraphicsDevice graphics,
              CommandList commandList,
              Texture[] textureMipLevels,
              int width,
              int height,
              bool isCompressed,
              ref Texture __result)
        {
            __result = null;
            if (textureMipLevels == null || textureMipLevels.Length == 0)
                return false;

            int mipCount = textureMipLevels.Length;

            //Assumes all textures are same format.
            PixelFormat format = textureMipLevels[0].Format;

            __result = Texture.New2D(graphics, width, height, mipCount, format);

            for (int j = 0; j < mipCount; j++)
            {
                commandList.CopyRegion(textureMipLevels[j], 0, null, __result, j);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(CustomOrbRenderData), nameof(CustomOrbRenderData.Initialize))]
    public class PatchCustomOrbRenderData
    {
        static bool Prefix(CustomOrbRenderData __instance, Game game,
              GraphicsDevice graphics,
              Orb orb,
              OrbSurfaceDrawType surfaceDrawType,
              OrbAtmosphereDrawType atmosphereDrawType,
              bool hasClouds,
              bool hasCityLights,
              bool hasRings,
              bool hasOrbitPath)
        {
            __instance.SurfaceDrawType = surfaceDrawType;
            __instance.AtmosphereDrawType = atmosphereDrawType;
            switch (__instance.SurfaceDrawType)
            {
                case OrbSurfaceDrawType.PlanetSolid:
                case OrbSurfaceDrawType.PlanetSolidContinents:
                case OrbSurfaceDrawType.PlanetSolidEmissiveOceans:
                case OrbSurfaceDrawType.PlanetGas:
                case OrbSurfaceDrawType.PlanetGasEmissive:
                case OrbSurfaceDrawType.Star:
                    EffectHelper.ObtainEffect(game, __instance.SurfaceDrawType);
                    break;
            }
            switch (__instance.AtmosphereDrawType)
            {
                case OrbAtmosphereDrawType.Haze:
                case OrbAtmosphereDrawType.Corona:
                    EffectHelper.ObtainEffect(game, __instance.AtmosphereDrawType);
                    break;
            }
            if (hasClouds)
            {
                EffectHelper.ObtainEffectClouds(game);
                EffectHelper.ObtainEffectCloudShadows(game);
            }
            if (hasCityLights)
            {
                EffectInstance effectCityLights = EffectHelper.ObtainEffectCityLights(game);
                Random rnd = new Random(orb.OrbId);
                __instance.GenerateCityLightsTexture(game, graphics, rnd, 1024, 512);

                effectCityLights.Parameters.SetObject((ParameterKey)CityLightsKeys.CityLightPatternSampler, (object)graphics.SamplerStates.LinearWrap);
            }
            if (hasRings)
                EffectHelper.ObtainEffectPlanetRings(game);
            if (!hasOrbitPath)
                return false;
            EffectHelper.ObtainEffectOrbitPath(game);
            return false;
        }
    }
}
