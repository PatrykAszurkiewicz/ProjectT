using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;


// Scene-safe object pool keyed by prefab.
// Purpose: kill the GC hitches from tower projectiles being Instantiate()'d and
// Destroy()'d dozens of times a second. Get() hands you a recycled instance (or
// makes a fresh one on the first request), Release() parks it for reuse.
// SCENE / SESSION SAFETY (this is the part that prevents "stale pooled object"
// regressions):
//   All pooled instances live in the ACTIVE scene, so a scene reload destroys
//     them exactly like the old Instantiate/Destroy flow did. On sceneLoaded we
//     clear the dictionaries, so we never hand out a destroyed instance from a
//     previous scene.
//   Get() also skips any entry that has gone fake-null, as a belt-and-braces
//     guard.
//   Release() on an object the pool doesn't recognise (e.g. its pool was
//     cleared by a scene load while it was mid-flight) just Destroys it — never
//     throws, never leaks it back into a dead pool.

public static class PrefabPool
{
    private class Entry
    {
        public GameObject prefab;
        public readonly Stack<GameObject> free = new Stack<GameObject>();
    }

    // prefab InstanceID -> its pool.
    private static readonly Dictionary<int, Entry> _pools = new Dictionary<int, Entry>();
    // pooled-instance InstanceID -> the pool it belongs to (so Release knows where
    // to put it, and so we can tell "ours" from "not ours").
    private static readonly Dictionary<int, Entry> _instanceToPool = new Dictionary<int, Entry>();

    private static Transform _container;
    private static bool _sceneHookInstalled;

    // Domain-reload-off safety: wipe everything at the very start of a Play session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _pools.Clear();
        _instanceToPool.Clear();
        _container = null;
        _sceneHookInstalled = false;
    }

    private static void EnsureSceneHook()
    {
        if (_sceneHookInstalled) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        _sceneHookInstalled = true;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The previous scene's container and every pooled/active instance were
        // destroyed with it. Drop all references so we start clean.
        _pools.Clear();
        _instanceToPool.Clear();
        _container = null;
    }

    private static Transform Container
    {
        get
        {
            if (_container == null)
            {
                var go = new GameObject("~PrefabPool");
                // NOT DontDestroyOnLoad on purpose: it must die with the scene so
                // pooled instances never survive into the next run.
                _container = go.transform;
            }
            return _container;
        }
    }


    // Get an instance of <paramref name="prefab"/> at the given pose. Returns an
    // active GameObject. Equivalent to Instantiate(prefab, pos, rot) but recycled.

    public static GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;
        EnsureSceneHook();

        int prefabId = prefab.GetInstanceID();
        if (!_pools.TryGetValue(prefabId, out var entry))
        {
            entry = new Entry { prefab = prefab };
            _pools[prefabId] = entry;
        }

        GameObject obj = null;
        while (entry.free.Count > 0)
        {
            var candidate = entry.free.Pop();
            if (candidate != null) { obj = candidate; break; } // skip fake-null leftovers
        }

        if (obj == null)
        {
            obj = Object.Instantiate(prefab, position, rotation);
            _instanceToPool[obj.GetInstanceID()] = entry;
        }
        else
        {
            obj.transform.SetParent(null, false);
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.SetActive(true);
        }

        return obj;
    }


    // Return a previously-pooled instance for reuse. Safe to call on anything:
    // unknown objects are simply Destroyed (matching the old lifecycle).

    public static void Release(GameObject obj)
    {
        if (obj == null) return;

        int id = obj.GetInstanceID();
        if (_instanceToPool.TryGetValue(id, out var entry))
        {
            // Idempotency guard: a live, in-flight instance is always active. If
            // it's already inactive it has already been released, so bail out —
            // this prevents a double-release from pushing the same object onto the
            // free stack twice (which could later hand one projectile to two
            // callers at once).
            if (!obj.activeSelf) return;

            obj.SetActive(false);
            obj.transform.SetParent(Container, false);
            entry.free.Push(obj);
        }
        else
        {
            // Came from somewhere we no longer track (e.g. pool cleared by a scene
            // load while this was in flight). Destroy rather than leak.
            Object.Destroy(obj);
        }
    }
}
