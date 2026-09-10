using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using mvcExpress.Internal.DependencyInjection;

namespace mvcExpress.PerformanceHarness
{
    [Serializable]
    internal struct DataStructureBenchmarkResult
    {
        public string category;
        public string variant;
        public string operation;
        public int tableSize;
        public int operations;
        public double totalMilliseconds;
        public double nanosecondsPerOperation;
        public long allocatedBytesDelta;
        public double bytesPerOperation;
        public bool correctnessCheckPassed;
    }

    internal static class ContainerDataStructureBenchmarks
    {
        private const int LookupIterations = 500000;
        private const int RegistrationBatches = 256;
        private static object _sink;

        internal static DataStructureBenchmarkResult[] Run()
        {
            var results = new List<DataStructureBenchmarkResult>();
            AddCollectionBenchmarks(results, 16);
            AddCollectionBenchmarks(results, 64);
            AddInterfaceClassificationBenchmarks(results);
            AddDelegateCacheBenchmarks(results);
            AddContainerBenchmarks(results);
            return results.ToArray();
        }

        private static void AddDelegateCacheBenchmarks(List<DataStructureBenchmarkResult> results)
        {
            var keys = new (Type, Type)[16];
            var concurrent = new ConcurrentDictionary<(Type, Type), Action>();
            var readTable = new Dictionary<(Type, Type), Action>();
            Action cachedDelegate = () => { };
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = (AllKeys[i], AllKeys[i + 16]);
                concurrent.TryAdd(keys[i], cachedDelegate);
                readTable.Add(keys[i], cachedDelegate);
            }

            bool correct = true;
            results.Add(Measure("Delegate metadata cache", "ConcurrentDictionary", "Warm tuple-key cache hit", keys.Length, LookupIterations, () =>
            {
                Action value = null;
                for (int i = 0; i < LookupIterations; i++)
                {
                    if (!concurrent.TryGetValue(keys[i & 15], out value)) correct = false;
                }
                _sink = value;
            }, () => correct && ReferenceEquals(_sink, cachedDelegate)));

            correct = true;
            results.Add(Measure("Delegate metadata cache", "Dictionary read table", "Warm tuple-key cache hit", keys.Length, LookupIterations, () =>
            {
                Action value = null;
                for (int i = 0; i < LookupIterations; i++)
                {
                    if (!readTable.TryGetValue(keys[i & 15], out value)) correct = false;
                }
                _sink = value;
            }, () => correct && ReferenceEquals(_sink, cachedDelegate)));
        }

        private static void AddCollectionBenchmarks(List<DataStructureBenchmarkResult> results, int tableSize)
        {
            var keys = new Type[tableSize];
            var values = new object[tableSize];
            Array.Copy(AllKeys, keys, tableSize);
            for (int i = 0; i < tableSize; i++) values[i] = new object();

            var concurrent = new ConcurrentDictionary<Type, object>();
            var readTable = new Dictionary<Type, object>();
            for (int i = 0; i < tableSize; i++)
            {
                concurrent.TryAdd(keys[i], values[i]);
                readTable.Add(keys[i], values[i]);
            }

            bool correct = true;
            results.Add(Measure("DI index", "ConcurrentDictionary", "Hit lookup", tableSize, LookupIterations, () =>
            {
                object value = null;
                for (int i = 0; i < LookupIterations; i++)
                {
                    if (!concurrent.TryGetValue(keys[i & (tableSize - 1)], out value)) correct = false;
                }
                _sink = value;
            }, () => correct && _sink != null));

            correct = true;
            results.Add(Measure("DI index", "Dictionary read table", "Hit lookup", tableSize, LookupIterations, () =>
            {
                object value = null;
                for (int i = 0; i < LookupIterations; i++)
                {
                    if (!readTable.TryGetValue(keys[i & (tableSize - 1)], out value)) correct = false;
                }
                _sink = value;
            }, () => correct && _sink != null));

            correct = true;
            results.Add(Measure("DI index", "ConcurrentDictionary", "Miss lookup", tableSize, LookupIterations, () =>
            {
                for (int i = 0; i < LookupIterations; i++)
                {
                    if (concurrent.TryGetValue(typeof(MissingKey), out _)) correct = false;
                }
            }, () => correct));

            correct = true;
            results.Add(Measure("DI index", "Dictionary read table", "Miss lookup", tableSize, LookupIterations, () =>
            {
                for (int i = 0; i < LookupIterations; i++)
                {
                    if (readTable.TryGetValue(typeof(MissingKey), out _)) correct = false;
                }
            }, () => correct));

            var sideTable = new ConcurrentDictionary<Type, object>();
            sideTable.TryAdd(typeof(MissingKey), values[0]);

            correct = true;
            results.Add(Measure("DI index", "Read table + concurrent side table", "Base hit lookup", tableSize, LookupIterations, () =>
            {
                object value = null;
                for (int i = 0; i < LookupIterations; i++)
                {
                    var key = keys[i & (tableSize - 1)];
                    if (!readTable.TryGetValue(key, out value) && !sideTable.TryGetValue(key, out value)) correct = false;
                }
                _sink = value;
            }, () => correct && _sink != null));

            correct = true;
            results.Add(Measure("DI index", "Read table + concurrent side table", "Side-table hit lookup", tableSize, LookupIterations, () =>
            {
                object value = null;
                for (int i = 0; i < LookupIterations; i++)
                {
                    if (!readTable.TryGetValue(typeof(MissingKey), out value) &&
                        !sideTable.TryGetValue(typeof(MissingKey), out value)) correct = false;
                }
                _sink = value;
            }, () => correct && _sink != null));

            int registrationOperations = RegistrationBatches * tableSize;
            results.Add(Measure("DI index", "ConcurrentDictionary", "Registration", tableSize, registrationOperations, () =>
            {
                ConcurrentDictionary<Type, object> table = null;
                for (int batch = 0; batch < RegistrationBatches; batch++)
                {
                    table = new ConcurrentDictionary<Type, object>();
                    for (int i = 0; i < tableSize; i++) table.TryAdd(keys[i], values[i]);
                }
                _sink = table;
            }, () => ((ConcurrentDictionary<Type, object>)_sink).Count == tableSize));

            results.Add(Measure("DI index", "Dictionary", "Registration", tableSize, registrationOperations, () =>
            {
                Dictionary<Type, object> table = null;
                for (int batch = 0; batch < RegistrationBatches; batch++)
                {
                    table = new Dictionary<Type, object>(tableSize);
                    for (int i = 0; i < tableSize; i++) table.Add(keys[i], values[i]);
                }
                _sink = table;
            }, () => ((Dictionary<Type, object>)_sink).Count == tableSize));

            results.Add(Measure("DI index", "Dictionary snapshot", "Build and publish snapshot", tableSize, registrationOperations, () =>
            {
                Dictionary<Type, object> snapshot = null;
                for (int batch = 0; batch < RegistrationBatches; batch++)
                {
                    var mutable = new Dictionary<Type, object>(tableSize);
                    for (int i = 0; i < tableSize; i++) mutable.Add(keys[i], values[i]);
                    snapshot = new Dictionary<Type, object>(mutable);
                }
                _sink = snapshot;
            }, () => ((Dictionary<Type, object>)_sink).Count == tableSize));
        }

        private static void AddInterfaceClassificationBenchmarks(List<DataStructureBenchmarkResult> results)
        {
            var types = new[]
            {
                typeof(IBenchmarkLogicService), typeof(LogicService),
                typeof(IBenchmarkViewService), typeof(ViewService)
            };
            var concurrent = new ConcurrentDictionary<Type, bool>();
            var readTable = new Dictionary<Type, bool>();
            for (int i = 0; i < types.Length; i++)
            {
                concurrent.TryAdd(types[i], types[i].IsInterface);
                readTable.Add(types[i], types[i].IsInterface);
            }

            bool last = false;
            bool correct = true;
            results.Add(Measure("Metadata cache", "Direct Type.IsInterface", "Classification", types.Length, LookupIterations, () =>
            {
                for (int i = 0; i < LookupIterations; i++) last = types[i & 3].IsInterface;
            }, () => last == types[(LookupIterations - 1) & 3].IsInterface));

            results.Add(Measure("Metadata cache", "ConcurrentDictionary", "Warm cache hit", types.Length, LookupIterations, () =>
            {
                for (int i = 0; i < LookupIterations; i++)
                {
                    if (!concurrent.TryGetValue(types[i & 3], out last)) correct = false;
                }
            }, () => correct && last == types[(LookupIterations - 1) & 3].IsInterface));

            correct = true;
            results.Add(Measure("Metadata cache", "ConcurrentDictionary.GetOrAdd", "Warm cache hit", types.Length, LookupIterations, () =>
            {
                for (int i = 0; i < LookupIterations; i++) last = concurrent.GetOrAdd(types[i & 3], type => type.IsInterface);
            }, () => last == types[(LookupIterations - 1) & 3].IsInterface));

            correct = true;
            results.Add(Measure("Metadata cache", "Dictionary read table", "Warm cache hit", types.Length, LookupIterations, () =>
            {
                for (int i = 0; i < LookupIterations; i++)
                {
                    if (!readTable.TryGetValue(types[i & 3], out last)) correct = false;
                }
            }, () => correct && last == types[(LookupIterations - 1) & 3].IsInterface));
        }

        private static void AddContainerBenchmarks(List<DataStructureBenchmarkResult> results)
        {
            var container = new MvcDiContainer();
            var logic = new LogicService();
            var view = new ViewService();
            container.Register(logic).ToLogic().ToLogicAs<IBenchmarkLogicService>().AsPermanent();
            container.Register(view).ToView().ToViewAs<IBenchmarkViewService>().AsPermanent();

            AddContainerLookup(results, container, typeof(LogicService), false, logic, "Logic concrete hit");
            AddContainerLookup(results, container, typeof(IBenchmarkLogicService), false, logic, "Logic interface hit");
            AddContainerLookup(results, container, typeof(ViewService), true, view, "View concrete hit");
            AddContainerLookup(results, container, typeof(IBenchmarkViewService), true, view, "View interface hit");
            AddContainerLookup(results, container, typeof(MissingKey), false, null, "Missing registration");

            container.Dispose();
        }

        private static void AddContainerLookup(
            List<DataStructureBenchmarkResult> results,
            MvcDiContainer container,
            Type type,
            bool useViewScope,
            object expected,
            string operation)
        {
            bool correct = true;
            results.Add(Measure("MvcDiContainer", "Current ConcurrentDictionary indexes", operation, 2, LookupIterations, () =>
            {
                object value = null;
                for (int i = 0; i < LookupIterations; i++)
                {
                    bool found = container.TryResolveInternal(type, useViewScope, null, out value);
                    if (found != (expected != null) || (found && !ReferenceEquals(value, expected))) correct = false;
                }
                _sink = value;
            }, () => correct));
        }

        private static DataStructureBenchmarkResult Measure(
            string category,
            string variant,
            string operation,
            int tableSize,
            int operations,
            Action measuredAction,
            Func<bool> correctnessCheck)
        {
            measuredAction();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var stopwatch = new Stopwatch();
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            stopwatch.Start();
            measuredAction();
            stopwatch.Stop();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            double nanoseconds = stopwatch.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency);

            return new DataStructureBenchmarkResult
            {
                category = category,
                variant = variant,
                operation = operation,
                tableSize = tableSize,
                operations = operations,
                totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                nanosecondsPerOperation = nanoseconds / operations,
                allocatedBytesDelta = allocatedBytes,
                bytesPerOperation = (double)allocatedBytes / operations,
                correctnessCheckPassed = correctnessCheck()
            };
        }

        private interface IBenchmarkLogicService { }
        private interface IBenchmarkViewService { }
        private sealed class LogicService : IBenchmarkLogicService { }
        private sealed class ViewService : IBenchmarkViewService { }
        private sealed class MissingKey { }

        private static readonly Type[] AllKeys =
        {
            typeof(Key00), typeof(Key01), typeof(Key02), typeof(Key03), typeof(Key04), typeof(Key05), typeof(Key06), typeof(Key07),
            typeof(Key08), typeof(Key09), typeof(Key10), typeof(Key11), typeof(Key12), typeof(Key13), typeof(Key14), typeof(Key15),
            typeof(Key16), typeof(Key17), typeof(Key18), typeof(Key19), typeof(Key20), typeof(Key21), typeof(Key22), typeof(Key23),
            typeof(Key24), typeof(Key25), typeof(Key26), typeof(Key27), typeof(Key28), typeof(Key29), typeof(Key30), typeof(Key31),
            typeof(Key32), typeof(Key33), typeof(Key34), typeof(Key35), typeof(Key36), typeof(Key37), typeof(Key38), typeof(Key39),
            typeof(Key40), typeof(Key41), typeof(Key42), typeof(Key43), typeof(Key44), typeof(Key45), typeof(Key46), typeof(Key47),
            typeof(Key48), typeof(Key49), typeof(Key50), typeof(Key51), typeof(Key52), typeof(Key53), typeof(Key54), typeof(Key55),
            typeof(Key56), typeof(Key57), typeof(Key58), typeof(Key59), typeof(Key60), typeof(Key61), typeof(Key62), typeof(Key63)
        };

        private sealed class Key00 { }
        private sealed class Key01 { }
        private sealed class Key02 { }
        private sealed class Key03 { }
        private sealed class Key04 { }
        private sealed class Key05 { }
        private sealed class Key06 { }
        private sealed class Key07 { }
        private sealed class Key08 { }
        private sealed class Key09 { }
        private sealed class Key10 { }
        private sealed class Key11 { }
        private sealed class Key12 { }
        private sealed class Key13 { }
        private sealed class Key14 { }
        private sealed class Key15 { }
        private sealed class Key16 { }
        private sealed class Key17 { }
        private sealed class Key18 { }
        private sealed class Key19 { }
        private sealed class Key20 { }
        private sealed class Key21 { }
        private sealed class Key22 { }
        private sealed class Key23 { }
        private sealed class Key24 { }
        private sealed class Key25 { }
        private sealed class Key26 { }
        private sealed class Key27 { }
        private sealed class Key28 { }
        private sealed class Key29 { }
        private sealed class Key30 { }
        private sealed class Key31 { }
        private sealed class Key32 { }
        private sealed class Key33 { }
        private sealed class Key34 { }
        private sealed class Key35 { }
        private sealed class Key36 { }
        private sealed class Key37 { }
        private sealed class Key38 { }
        private sealed class Key39 { }
        private sealed class Key40 { }
        private sealed class Key41 { }
        private sealed class Key42 { }
        private sealed class Key43 { }
        private sealed class Key44 { }
        private sealed class Key45 { }
        private sealed class Key46 { }
        private sealed class Key47 { }
        private sealed class Key48 { }
        private sealed class Key49 { }
        private sealed class Key50 { }
        private sealed class Key51 { }
        private sealed class Key52 { }
        private sealed class Key53 { }
        private sealed class Key54 { }
        private sealed class Key55 { }
        private sealed class Key56 { }
        private sealed class Key57 { }
        private sealed class Key58 { }
        private sealed class Key59 { }
        private sealed class Key60 { }
        private sealed class Key61 { }
        private sealed class Key62 { }
        private sealed class Key63 { }
    }
}
