﻿// Partial class root for MvcMessageBus - holds no members itself.
// All implementation is split across:
//   MvcMessageBus.cs          (Internal/Messaging) - core state, disposal, statistics, instance ID recycling
//   MvcMessageBus.Params00.cs - 0-parameter message overloads (Storage0, Subscribe, Publish, Unsubscribe)
//   MvcMessageBus.Params01.cs … Params12.cs - arity variants 1-12
// See MvcMessageBus.Params00.cs for the template pattern shared by all arity variants.
using mvcExpress.Internal.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace mvcExpress.Internal.Messaging
{
    public sealed partial class MvcMessageBus : IMessageBus, IDisposable
    {







    }
}
