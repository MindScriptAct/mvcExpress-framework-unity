using mvcExpress.Internal.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Internal command processor contract combining public command APIs used by modules and generated binders.
    /// </summary>
    public interface ICommandProcessorInternal : ICommandProcessor, ICommandAsyncBinder
    {
        /// <summary>
        /// Cancelled when this processor's owning module is destroyed. <see cref="CancellationToken.None"/>
        /// if this processor was constructed without an owning module (e.g. in isolated tests).
        /// </summary>
        CancellationToken CancelToken { get; }
    }
}
