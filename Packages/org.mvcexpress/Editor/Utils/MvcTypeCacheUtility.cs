using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace mvcExpress.Editor.Core
{
    internal static class MvcTypeCacheUtility
    {
        public static List<Type> GetNonAbstractDerivedTypes<TBase>()
        {
            return TypeCache.GetTypesDerivedFrom<TBase>()
                .Where(t => t != null && !t.IsAbstract && !t.IsGenericTypeDefinition)
                .OrderBy(t => t.FullName)
                .ToList();
        }

        public static List<Type> GetInterfacesAssignableFrom(Type concreteType)
        {
            if (concreteType == null) return new List<Type>(0);

            return concreteType.GetInterfaces()
                .Where(i => i != null)
                .OrderBy(i => i.FullName)
                .ToList();
        }

        public static bool IsFrameworkInterface(Type i)
        {
            if (i == null || !i.IsInterface) return true;

            var ns = i.Namespace ?? string.Empty;
            if (ns.StartsWith("System")) return true;
            if (ns.StartsWith("UnityEngine")) return true;
            if (ns.StartsWith("UnityEditor")) return true;
            if (ns.StartsWith("mvcExpress")) return true;

            return false;
        }
    }
}
