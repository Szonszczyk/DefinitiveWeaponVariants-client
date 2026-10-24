using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using DefinitiveWeaponVariants.Client.Effects;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using EFT.UI.WeaponModding;
using SPT.Reflection.Patching;
using UnityEngine;

namespace DefinitiveWeaponVariants.Client.Patches
{
    internal sealed class ItemColorPreviewPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(WeaponPreview).GetMethod(
            nameof(WeaponPreview.PositionGameObject), new[] { typeof(GameObject), typeof(Vector3?) })
            ?? throw new MissingMethodException("WeaponPreview.PositionGameObject");

        [PatchPostfix]
        private static void Postfix(GameObject __0, Item ____currentItem) =>
            ItemColorEffect.Instance?.Attach(__0, ____currentItem?.StringTemplateId);
    }

    internal sealed class ItemColorIconPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(ItemIconCreator).GetMethod(
            nameof(ItemIconCreator.RenderModel), new[] { typeof(Item), typeof(IconCreatorBase<Item, ItemIcon>.SpriteFactory) })
            ?? throw new MissingMethodException("ItemIconCreator.RenderModel");

        [PatchPrefix]
        private static void Prefix(Item item, ref IconCreatorBase<Item, ItemIcon>.SpriteFactory spriteFactory, out RenderState __state)
        {
            __state = null;
            var effect = ItemColorEffect.Instance;
            if (!effect || item == null || (effect.Ready.IsCompleted && !effect.Contains(item.StringTemplateId))) return;
            __state = new RenderState(effect, item.StringTemplateId, spriteFactory);
            spriteFactory = __state.Render;
        }

        [PatchPostfix]
        private static void Postfix(ref Task<IconCreatorBase<Item, ItemIcon>.RenderModelResult> __result, RenderState __state)
        {
            if (__state != null) __result = __state.Complete(__result);
        }

        internal sealed class RenderState
        {
            private readonly ItemColorEffect _effect;
            private readonly string _templateId;
            private readonly IconCreatorBase<Item, ItemIcon>.SpriteFactory _factory;
            private bool _applied;
            private Sprite _sprite;
            internal RenderState(ItemColorEffect effect, string templateId, IconCreatorBase<Item, ItemIcon>.SpriteFactory factory)
            { _effect = effect; _templateId = templateId; _factory = factory; }

            internal async Task<Sprite> Render(GameObject model, PreviewPivot pivot)
            {
                // No ConfigureAwait(false): all Unity work must resume on its main thread.
                await _effect.Ready;
                _applied = _effect && _effect.Attach(model, _templateId);
                try { return _sprite = await _factory(model, pivot); }
                finally { if (model) model.GetComponent<ItemColorModel>()?.Release(); }
            }

            internal async Task<IconCreatorBase<Item, ItemIcon>.RenderModelResult> Complete(
                Task<IconCreatorBase<Item, ItemIcon>.RenderModelResult> original)
            {
                var result = await original;
                // EFT may select PreviewPivot.Icon.overrideIcon AFTER returning the model to
                // its pool. Select the rendered sprite here without mutating shared pivot settings.
                if (_applied && _sprite) result.sprite = _sprite;
                return result;
            }
        }
    }

    internal sealed class ItemColorGameObjectPoolPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(AssetPoolObject).GetMethod(
            nameof(AssetPoolObject.ReturnToPool), new[] { typeof(GameObject), typeof(bool) });
        [PatchPrefix]
        private static void Prefix(GameObject __0) => Release(__0);

        internal static void Release(GameObject root)
        {
            if (!root) return;
            foreach (var model in root.GetComponentsInChildren<ItemColorModel>(true)) model.Release();
        }
    }

    internal sealed class ItemColorIconShaderPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(ShaderReplacer).GetMethod(
            nameof(ShaderReplacer.Replace), new[] { typeof(GameObject) });

        [PatchPrefix]
        private static void Prefix(GameObject __0, out List<Renderer> __state)
        {
            __state = null;
            if (!__0) return;
            var models = __0.GetComponentsInChildren<ItemColorModel>(true);
            if (models.Length == 0) return;
            __state = new List<Renderer>();
            foreach (var model in models) model.SuspendForShaderReplacement(__state);
        }

        // ShaderReplacer scans enabled renderers. Skipping only our overlays preserves their
        // transparent shader and avoids creating material instances that the game would not own.
        [PatchPostfix]
        private static void Postfix(List<Renderer> __state)
        {
            if (__state == null) return;
            foreach (var renderer in __state) if (renderer) renderer.enabled = true;
        }
    }

    internal sealed class ItemColorAssetPoolPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(AssetPoolObject).GetMethod(
            nameof(AssetPoolObject.ReturnToPool), new[] { typeof(AssetPoolObject) });
        [PatchPrefix]
        private static void Prefix(AssetPoolObject __0)
        { if (__0) ItemColorGameObjectPoolPatch.Release(__0.gameObject); }
    }
}
