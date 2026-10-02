using SodCoop.Net;
using UnityEngine;

namespace SodCoop
{
    /// <summary>
    /// Another player, drawn as a coloured capsule with a block showing where they are looking.
    /// It has no collider, so NPCs and the local player walk through it.
    /// </summary>
    internal sealed class RemoteAvatar
    {
        static readonly Color[] Palette =
        {
            new Color(0.95f, 0.75f, 0.2f),
            new Color(0.3f, 0.7f, 0.95f),
            new Color(0.9f, 0.35f, 0.35f),
            new Color(0.45f, 0.85f, 0.45f),
        };

        public readonly byte PlayerId;
        public string Name;
        public readonly SnapshotBuffer Buffer = new SnapshotBuffer();

        GameObject _root;
        Transform _head;

        public RemoteAvatar(byte playerId, string name)
        {
            PlayerId = playerId;
            Name = name;
        }

        public bool HasPosition { get; private set; }
        public Vector3 HeadPosition => _head != null ? _head.position : Vector3.zero;

        public void Update(double renderTime, float heightOffset)
        {
            var state = Buffer.Sample(renderTime);
            if (state == null)
                return;

            EnsureCreated();
            _root.transform.position = new Vector3(state.X, state.Y, state.Z);
            _root.transform.rotation = Quaternion.Euler(0, state.Yaw, 0);
            _root.transform.GetChild(0).localPosition = new Vector3(0, heightOffset, 0);
            _head.localPosition = new Vector3(0, heightOffset + 0.7f, 0.25f);
            _head.localRotation = Quaternion.Euler(state.Pitch, 0, 0);
            HasPosition = true;
        }

        void EnsureCreated()
        {
            if (_root != null)
                return;

            _root = new GameObject($"SodCoop Player {PlayerId}");
            var color = Palette[PlayerId % Palette.Length];

            var body = MakePart(PrimitiveType.Capsule, "Body");
            body.transform.SetParent(_root.transform, false);
            body.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            Tint(body, color);

            var head = MakePart(PrimitiveType.Cube, "Head");
            head.transform.SetParent(_root.transform, false);
            head.transform.localScale = new Vector3(0.25f, 0.15f, 0.3f);
            Tint(head, Color.black);
            _head = head.transform;
        }

        static bool _primitivesBroken;

        /// <summary>
        /// A visible shape without a collider. If the game build stripped CreatePrimitive, falls back
        /// to an empty object so the player's name label still shows.
        /// </summary>
        static GameObject MakePart(PrimitiveType shape, string name)
        {
            if (!_primitivesBroken)
            {
                try
                {
                    var go = GameObject.CreatePrimitive(shape);
                    go.name = name;
                    var collider = go.GetComponent<Collider>();
                    if (collider != null)
                        Object.Destroy(collider);
                    return go;
                }
                catch (System.Exception ex)
                {
                    _primitivesBroken = true;
                    Plugin.CoopLog.LogWarning("Cannot create player shapes, showing name labels only: " + ex.GetBaseException().Message);
                }
            }
            return new GameObject(name);
        }

        static void Tint(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = color;
        }

        public void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _head = null;
            HasPosition = false;
        }
    }
}
