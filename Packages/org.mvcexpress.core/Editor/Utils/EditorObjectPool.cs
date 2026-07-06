using System;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Generic object pooling utility for editor collections to reduce GC allocations.
    /// PERFORMANCE: Reuses collections instead of creating new ones on every operation.
    /// </summary>
    public static class EditorObjectPool
    {
        // Pool for List<Type>
        private static readonly Stack<List<Type>> s_typeListPool = new Stack<List<Type>>(8);
        private const int MaxTypeListCapacity = 512;

        // Pool for Dictionary<Type, int>
        private static readonly Stack<Dictionary<Type, int>> s_typeCountDictPool = new Stack<Dictionary<Type, int>>(8);
        private const int MaxDictCapacity = 128;

        // Pool for List<MonoBehaviour>
        private static readonly Stack<List<MonoBehaviour>> s_monoBehaviourListPool = new Stack<List<MonoBehaviour>>(8);
        private const int MaxMonoBehaviourListCapacity = 256;

        // Pool for List<ProxyBehaviour>
        private static readonly Stack<List<ProxyBehaviour>> s_proxyBehaviourListPool = new Stack<List<ProxyBehaviour>>(8);
        private const int MaxProxyBehaviourListCapacity = 256;

        #region List<Type> Pooling

        public static List<Type> RentTypeList()
        {
            if (s_typeListPool.Count > 0)
            {
                var list = s_typeListPool.Pop();
                list.Clear();
                return list;
            }

            return new List<Type>(64);
        }

        public static void ReturnTypeList(List<Type> list)
        {
            if (list == null)
                return;

            if (s_typeListPool.Count < 8)
            {
                list.Clear();
                
                if (list.Capacity > MaxTypeListCapacity)
                {
                    list.Capacity = MaxTypeListCapacity;
                }

                s_typeListPool.Push(list);
            }
        }

        #endregion

        #region Dictionary<Type, int> Pooling

        public static Dictionary<Type, int> RentTypeCountDict()
        {
            if (s_typeCountDictPool.Count > 0)
            {
                var dict = s_typeCountDictPool.Pop();
                dict.Clear();
                return dict;
            }

            return new Dictionary<Type, int>(32);
        }

        public static void ReturnTypeCountDict(Dictionary<Type, int> dict)
        {
            if (dict == null)
                return;

            if (s_typeCountDictPool.Count < 8)
            {
                dict.Clear();
                s_typeCountDictPool.Push(dict);
            }
        }

        #endregion

        #region List<MonoBehaviour> Pooling

        public static List<MonoBehaviour> RentMonoBehaviourList()
        {
            if (s_monoBehaviourListPool.Count > 0)
            {
                var list = s_monoBehaviourListPool.Pop();
                list.Clear();
                return list;
            }

            return new List<MonoBehaviour>(64);
        }

        public static void ReturnMonoBehaviourList(List<MonoBehaviour> list)
        {
            if (list == null)
                return;

            if (s_monoBehaviourListPool.Count < 8)
            {
                list.Clear();
                
                if (list.Capacity > MaxMonoBehaviourListCapacity)
                {
                    list.Capacity = MaxMonoBehaviourListCapacity;
                }

                s_monoBehaviourListPool.Push(list);
            }
        }

        #endregion

        #region List<ProxyBehaviour> Pooling

        public static List<ProxyBehaviour> RentProxyBehaviourList()
        {
            if (s_proxyBehaviourListPool.Count > 0)
            {
                var list = s_proxyBehaviourListPool.Pop();
                list.Clear();
                return list;
            }

            return new List<ProxyBehaviour>(64);
        }

        public static void ReturnProxyBehaviourList(List<ProxyBehaviour> list)
        {
            if (list == null)
                return;

            if (s_proxyBehaviourListPool.Count < 8)
            {
                list.Clear();
                
                if (list.Capacity > MaxProxyBehaviourListCapacity)
                {
                    list.Capacity = MaxProxyBehaviourListCapacity;
                }

                s_proxyBehaviourListPool.Push(list);
            }
        }

        #endregion

        public static void ClearAllPools()
        {
            s_typeListPool.Clear();
            s_typeCountDictPool.Clear();
            s_monoBehaviourListPool.Clear();
            s_proxyBehaviourListPool.Clear();
        }

        public static string GetPoolStats()
        {
            return $"EditorObjectPool Stats:\n" +
                   $"  List<Type>: {s_typeListPool.Count} pooled\n" +
                   $"  Dict<Type,int>: {s_typeCountDictPool.Count} pooled\n" +
                   $"  List<MonoBehaviour>: {s_monoBehaviourListPool.Count} pooled\n" +
                   $"  List<ProxyBehaviour>: {s_proxyBehaviourListPool.Count} pooled";
        }
    }
}
