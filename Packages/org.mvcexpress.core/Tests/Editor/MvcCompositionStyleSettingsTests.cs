using mvcExpress.Editor.Settings;
using NUnit.Framework;

namespace mvcExpress.Editor.Tests
{
    /// <summary>
    /// Covers CMP-04/EDT-01 (composition-style policy moved off per-machine EditorPrefs onto a
    /// project-owned asset) and EDT-05 (hard-enforcement define sync applies across every build
    /// target group, not just the selected one). See
    /// InternalSpecs/ReleaseAudit_260910/RESOLVED/CMP-04_composition_settings_editorprefs_not_project_policy.md.
    /// </summary>
    public class MvcCompositionStyleSettingsTests
    {
        private static readonly MvcCompositionStyle[] AllStyles =
        {
            MvcCompositionStyle.Unity, MvcCompositionStyle.Attribute, MvcCompositionStyle.Code
        };

        // Captured before each test and restored after, so exercising the real, project-owned
        // backing store never leaves this developer's actual project state changed.
        private bool[] _originalSoft;
        private bool[] _originalHard;

        [SetUp]
        public void SetUp()
        {
            _originalSoft = new bool[AllStyles.Length];
            _originalHard = new bool[AllStyles.Length];
            for (int i = 0; i < AllStyles.Length; i++)
            {
                _originalSoft[i] = MvcCompositionStyleSettings.IsSoftAllowed(AllStyles[i]);
                _originalHard[i] = MvcCompositionStyleSettings.IsHardIncluded(AllStyles[i]);
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < AllStyles.Length; i++)
            {
                MvcCompositionStyleSettings.SetSoftAllowed(AllStyles[i], _originalSoft[i]);
                MvcCompositionStyleSettings.SetHardIncluded(AllStyles[i], _originalHard[i]);
            }
        }

        // ── Storage round-trips through the project-owned asset, not EditorPrefs ────────────

        [Test]
        public void SetSoftAllowed_ThenIsSoftAllowed_RoundTripsPerStyle()
        {
            foreach (var style in AllStyles)
            {
                MvcCompositionStyleSettings.SetSoftAllowed(style, false);
                Assert.That(MvcCompositionStyleSettings.IsSoftAllowed(style), Is.False,
                    $"{style}: SetSoftAllowed(false) must be reflected by IsSoftAllowed.");

                MvcCompositionStyleSettings.SetSoftAllowed(style, true);
                Assert.That(MvcCompositionStyleSettings.IsSoftAllowed(style), Is.True,
                    $"{style}: SetSoftAllowed(true) must be reflected by IsSoftAllowed.");
            }
        }

        [Test]
        public void SetHardIncluded_ThenIsHardIncluded_RoundTripsPerStyle()
        {
            foreach (var style in AllStyles)
            {
                MvcCompositionStyleSettings.SetHardIncluded(style, false);
                Assert.That(MvcCompositionStyleSettings.IsHardIncluded(style), Is.False,
                    $"{style}: SetHardIncluded(false) must be reflected by IsHardIncluded.");

                MvcCompositionStyleSettings.SetHardIncluded(style, true);
                Assert.That(MvcCompositionStyleSettings.IsHardIncluded(style), Is.True,
                    $"{style}: SetHardIncluded(true) must be reflected by IsHardIncluded.");
            }
        }

        [Test]
        public void SettingOneStyle_DoesNotChangeTheOtherTwo()
        {
            MvcCompositionStyleSettings.SetSoftAllowed(MvcCompositionStyle.Unity, false);
            MvcCompositionStyleSettings.SetHardIncluded(MvcCompositionStyle.Unity, false);

            Assert.That(MvcCompositionStyleSettings.IsSoftAllowed(MvcCompositionStyle.Attribute), Is.True,
                "Each style's policy must be stored independently - the new asset-backed store must " +
                "not collapse all three styles onto shared fields.");
            Assert.That(MvcCompositionStyleSettings.IsHardIncluded(MvcCompositionStyle.Code), Is.True);
        }

        // ── Two-restricted-styles-locks-the-third invariant still holds on the new store ────

        [Test]
        public void GetLockedStyle_TwoStylesRestricted_ReturnsTheThird()
        {
            MvcCompositionStyleSettings.SetSoftAllowed(MvcCompositionStyle.Unity, false);
            MvcCompositionStyleSettings.SetSoftAllowed(MvcCompositionStyle.Attribute, false);

            Assert.That(MvcCompositionStyleSettings.GetLockedStyle(), Is.EqualTo(MvcCompositionStyle.Code));
        }

        [Test]
        public void GetLockedStyle_NoStyleRestricted_ReturnsNull()
        {
            Assert.That(MvcCompositionStyleSettings.GetLockedStyle(), Is.Null);
        }

        // ── MvcCompositionDefineSymbols: pure define-list logic (no real PlayerSettings touched) ──

        [Test]
        public void TryComputeUpdatedDefines_HardIncludedFalse_SymbolMissing_AddsSymbol()
        {
            var changed = MvcCompositionDefineSymbols.TryComputeUpdatedDefines(
                "SOME_OTHER_DEFINE", "MVC_EXPRESS_NO_CODE", hardIncluded: false, out var updated);

            Assert.That(changed, Is.True);
            Assert.That(updated.Split(';'), Does.Contain("MVC_EXPRESS_NO_CODE"));
            Assert.That(updated.Split(';'), Does.Contain("SOME_OTHER_DEFINE"));
        }

        [Test]
        public void TryComputeUpdatedDefines_HardIncludedTrue_SymbolPresent_RemovesSymbol()
        {
            var changed = MvcCompositionDefineSymbols.TryComputeUpdatedDefines(
                "SOME_OTHER_DEFINE;MVC_EXPRESS_NO_CODE", "MVC_EXPRESS_NO_CODE", hardIncluded: true, out var updated);

            Assert.That(changed, Is.True);
            Assert.That(updated.Split(';'), Does.Not.Contain("MVC_EXPRESS_NO_CODE"));
            Assert.That(updated.Split(';'), Does.Contain("SOME_OTHER_DEFINE"));
        }

        [Test]
        public void TryComputeUpdatedDefines_AlreadyInTargetState_ReportsNoChange()
        {
            var stillIncluded = MvcCompositionDefineSymbols.TryComputeUpdatedDefines(
                "SOME_OTHER_DEFINE", "MVC_EXPRESS_NO_CODE", hardIncluded: true, out var updated1);
            Assert.That(stillIncluded, Is.False,
                "Symbol already absent and hardIncluded=true (nothing to remove) must report no change, " +
                "so callers can skip writing PlayerSettings unnecessarily.");
            Assert.That(updated1, Is.EqualTo("SOME_OTHER_DEFINE"));

            var stillExcluded = MvcCompositionDefineSymbols.TryComputeUpdatedDefines(
                "SOME_OTHER_DEFINE;MVC_EXPRESS_NO_CODE", "MVC_EXPRESS_NO_CODE", hardIncluded: false, out var updated2);
            Assert.That(stillExcluded, Is.False,
                "Symbol already present and hardIncluded=false (nothing to add) must report no change.");
            Assert.That(updated2, Is.EqualTo("SOME_OTHER_DEFINE;MVC_EXPRESS_NO_CODE"));
        }

        // ── Apply/SyncAll must not throw across every build target group on a real Editor ───
        // Exercised only in the default (fully-included) state, which is a guaranteed no-op for
        // every group's defines - see TryComputeUpdatedDefines_AlreadyInTargetState_ReportsNoChange
        // above - so this never writes to this developer's real PlayerSettings/ProjectSettings.asset.

        [Test]
        public void SyncAll_DefaultAllStylesIncluded_DoesNotThrowForAnyBuildTargetGroup()
        {
            foreach (var style in AllStyles)
            {
                MvcCompositionStyleSettings.SetHardIncluded(style, true);
            }

            Assert.DoesNotThrow(() => MvcCompositionDefineSymbols.SyncAll(),
                "SyncAll() now iterates every BuildTargetGroup (EDT-05) instead of only the " +
                "selected one - obsolete/uninstalled groups must be skipped, not thrown out of the loop.");
        }
    }
}
