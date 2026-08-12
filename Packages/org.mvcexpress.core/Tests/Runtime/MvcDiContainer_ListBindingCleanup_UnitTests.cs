using System;
using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class MvcDiContainer_ListBindingCleanup_UnitTests
    {
        private static class GcHelper
        {
            public static void ForceCollect()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private sealed class ListMember
        {
        }

        [Test]
        public void Clear_WithListBoundMembers_ReleasesStrongReferences()
        {
            var container = new MvcDiContainer();
            WeakReference weakMember = RegisterListMemberAndDropLocalRef(container);

            container.Clear();
            container.Dispose();
            GcHelper.ForceCollect();

            Assert.That(weakMember.IsAlive, Is.False,
                "Clear() must release _logicListMembers/_viewListMembers so a list-bound instance " +
                "becomes collectable once the container itself has no other referrers. Before the M1 fix " +
                "these maps were never cleared, retaining every instance ever added via ToLogicList/ToViewList.");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference RegisterListMemberAndDropLocalRef(MvcDiContainer container)
        {
            var member = new ListMember();
            container.Register(member).ToLogicList<ListMember>().AsPermanent();
            return new WeakReference(member);
        }
    }
}
