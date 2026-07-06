using NUnit.Framework;

namespace mvcExpress.Tests
{
    public class RegistrationLifecycleTests
    {
        [Test]
        public void RegistrationLifecycle_HasExpectedNumericValues()
        {
            Assert.That((int)RegistrationLifecycle.Permanent, Is.EqualTo(0));
            Assert.That((int)RegistrationLifecycle.Transient, Is.EqualTo(1));
            Assert.That((int)RegistrationLifecycle.Scoped, Is.EqualTo(2));
        }
    }
}
