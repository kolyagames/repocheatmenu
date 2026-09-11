using System;
using System.Collections.Generic;
using System.Reflection;

namespace RepoCheatMenu;

// The compile-time game-libs package marks private fields as public only in the reference
// stub used by the compiler. The real Assembly-CSharp.dll on disk still has them private, and
// Mono enforces that at runtime, so plain dot-syntax access throws FieldAccessException for
// any field that's actually non-public (caught silently by our per-tick try/catch, which is
// why toggles updated config but never touched real game state). Reflection's NonPublic flag
// bypasses that check, so every game-object field touch goes through here instead.
internal static class GameReflect
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new();
    private static readonly Dictionary<(Type, string), MethodInfo> _methodCache = new();

    private static FieldInfo Field(Type type, string name)
    {
        var key = (type, name);
        if (_fieldCache.TryGetValue(key, out FieldInfo cached)) return cached;

        // Type.GetField with NonPublic only ever returns members DECLARED on that exact type -
        // unlike public members, non-public fields are not "inherited" by a single reflection
        // call. A private/protected field declared on a BASE class (e.g. Photon.Realtime.Room's
        // "name" lives in its base RoomInfo, not Room itself) needs GetField called again on
        // each ancestor in turn, or it's silently never found - caught while wiring up
        // ScriptGlobals.RenameRoom, but this walk benefits every other GameReflect call in the
        // mod that happens to target an inherited field, not just that one.
        FieldInfo field = null;
        for (Type t = type; t != null && field == null; t = t.BaseType)
            field = t.GetField(name, Flags);

        _fieldCache[key] = field;
        return field;
    }

    public static T Get<T>(object instance, string fieldName)
    {
        if (instance == null) return default;
        FieldInfo field = Field(instance.GetType(), fieldName);
        if (field == null) return default;
        return (T)field.GetValue(instance);
    }

    public static void Set(object instance, string fieldName, object value)
    {
        if (instance == null) return;
        FieldInfo field = Field(instance.GetType(), fieldName);
        field?.SetValue(instance, value);
    }

    public static void Invoke(object instance, string methodName, params object[] args)
    {
        if (instance == null) return;
        Type type = instance.GetType();
        var key = (type, methodName);
        if (!_methodCache.TryGetValue(key, out MethodInfo method))
        {
            // Same inherited-non-public gap as Field() above - GetMethod on the exact runtime
            // type misses a private/protected method actually declared on a base class.
            method = null;
            for (Type t = type; t != null && method == null; t = t.BaseType)
                method = t.GetMethod(methodName, Flags);
            _methodCache[key] = method;
        }
        method?.Invoke(instance, args);
    }
}
