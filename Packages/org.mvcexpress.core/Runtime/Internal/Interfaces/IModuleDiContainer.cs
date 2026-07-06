using mvcExpress.Internal.DependencyInjection;
using UnityEngine;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Module-specific dependency injection container combining registration and resolution.
    /// </summary>
    public interface IModuleDiContainer : IModuleDependencyRegistrar, IModuleDependencyResolver { }
}
