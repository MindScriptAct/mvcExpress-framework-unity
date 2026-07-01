using mvcExpress.Internal.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Internal command processor contract combining public command APIs used by modules and generated binders.
    /// </summary>
    public interface ICommandProcessorInternal : ICommandProcessor, ICommandAsyncBinder { }
}
