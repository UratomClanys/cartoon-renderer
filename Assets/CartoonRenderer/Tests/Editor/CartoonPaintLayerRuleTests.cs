using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CartoonProjection.Editor
{
    public sealed class CartoonPaintLayerRuleTests
    {
        [Test]
        public void EyeShadowMaterialSuggestsHeroDetailAndFixedBase()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "MAT_Lucy_EyeShadow_UI" };
            var report = new CartoonColorRegionBaker.RendererReport
            {
                name = "EyeShadow",
                materials = new[] { material }
            };
            var rules = CartoonColorRegionBaker.BuildDefaultRules(new() { report });
            Assert.AreEqual(1, rules.Count);
            Assert.AreEqual(CartoonRegionPolicy.HeroDetail, rules[0].policy,
                "Eye shadow must fall into the HeroDetail family.");
            Assert.IsTrue(rules[0].fixedBaseLayer,
                "Painted-shadow materials must be pinned to the base paint layer.");
            Object.DestroyImmediate(material);
        }

        [Test]
        public void HeroDetailPolicyDefaultsToFixedBaseButStaysOverridable()
        {
            var rule = new CartoonMaterialRegionRule { materialName = "Face" };
            rule.ApplyPolicyDefaults(CartoonRegionPolicy.HeroDetail);
            Assert.IsTrue(rule.fixedBaseLayer, "Hero materials default to fixed base (painted look).");
            rule.fixedBaseLayer = false; // manual override must be possible and respected
            Assert.IsFalse(rule.fixedBaseLayer);
        }

        [Test]
        public void DefaultPaintLayerModeIsShadowBase()
        {
            var settings = new CartoonRenderSettings();
            Assert.AreEqual(CartoonPaintLayerMode.ShadowBase, settings.paintLayerMode);
            Assert.IsTrue(settings.paintLayerHysteresis, "Hysteresis must default on for animation stability.");
        }

        [Test]
        public void HysteresisExitThresholdsAreConsistent()
        {
            var settings = new CartoonRenderSettings();
            Assert.Greater(settings.shadowThresholdExit, settings.shadowThresholdEnter,
                "Leaving shadow requires a higher NdotL than entering it.");
            Assert.Less(settings.highlightThresholdExit, settings.highlightThresholdEnter,
                "Leaving highlight requires a lower NdotL than entering it.");
        }
    }
}
