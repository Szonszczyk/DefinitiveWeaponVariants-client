using System;
using UnityEngine;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal static class SurfaceOverlayMaterial
    {
        internal static Material Create(string name, Texture texture)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (!shader || !shader.isSupported) shader = Shader.Find("Unlit/Transparent");
            if (!shader || !shader.isSupported)
                throw new NotSupportedException("Neither Sprites/Default nor Unlit/Transparent is available.");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave, renderQueue = 3001 };
            material.mainTexture = texture;
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            return material;
        }

        internal static bool IsOpaqueBody(Material material)
        {
            if (!material || !material.shader || material.renderQueue >= 2500) return false;
            string name = (material.name + " " + material.shader.name).ToLowerInvariant();
            string[] excluded = { "glass", "lens", "linza", "reticle", "collimator", "optic", "transparent", "particle", "additive", "muzzle", "flare" };
            foreach (string token in excluded) if (name.Contains(token)) return false;
            return true;
        }
    }
}
