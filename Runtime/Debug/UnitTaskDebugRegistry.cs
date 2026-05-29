using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace UGS.UnitTask
{
    public static class UnitTaskDebugRegistry
    {
        private static readonly object s_lock = new object();
        private static readonly List<IUnitTaskDebugSnapshotSource> s_sources = new List<IUnitTaskDebugSnapshotSource>(8);

        public static void Register(IUnitTaskDebugSnapshotSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            lock (s_lock)
            {
                for (var i = 0; i < s_sources.Count; i++)
                {
                    if (ReferenceEquals(s_sources[i], source))
                    {
                        return;
                    }
                }

                s_sources.Add(source);
            }
        }

        public static void Unregister(IUnitTaskDebugSnapshotSource source)
        {
            if (source == null) return;

            lock (s_lock)
            {
                for (var i = s_sources.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(s_sources[i], source))
                    {
                        s_sources.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        public static IUnitTaskDebugSnapshotSource[] GetSources()
        {
            lock (s_lock)
            {
                if (s_sources.Count == 0)
                {
                    return Array.Empty<IUnitTaskDebugSnapshotSource>();
                }

                var result = new IUnitTaskDebugSnapshotSource[s_sources.Count];
                s_sources.CopyTo(result);
                return result;
            }
        }

        public static string GetEnumDescription(this Enum value)
        {
            FieldInfo field = value.GetType().GetField(value.ToString());
            return Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is not DescriptionAttribute attribute
                ? value.ToString()
                : attribute.Description;
        }
    }
}

