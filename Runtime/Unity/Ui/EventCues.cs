using System;
using System.Collections.Generic;
using Mosaic.Core;
using UnityEngine;
using EntityId = Mosaic.Core.EntityId;

namespace Mosaic.Ui
{
    /// <summary>
    /// What to play when something happens: a sound, an effect prefab, or both. Filled in by designers on a config
    /// asset; an empty cue plays nothing.
    /// </summary>
    [Serializable]
    public sealed class Cue
    {
        public AudioClip Sound;
        [Range(0, 1)] public float Volume = 1;

        [Tooltip("Instantiated from a pool at the entity's view and returned after Lifetime seconds of game time")]
        public GameObject Effect;
        public float Lifetime = 1;
        public Vector3 Offset;

        [Tooltip("Real seconds before the same cue may play again (a shotgun of ten pellets plays one hit sound)")]
        public float MinInterval;

        public bool IsEmpty => Sound == null && Effect == null;
    }

    /// <summary>
    /// Sounds and effects from domain events, one line per binding:
    /// <c>cues.On&lt;DamageApplied&gt;(e => e.Target, config.Hit)</c>. The position is the view of the entity the
    /// event names; without a view the sound still plays and the effect is skipped. Listen through an EventFeed of
    /// the screen, call <see cref="LateTick"/> with the game step so effects pause with the game, dispose with the
    /// screen.
    /// </summary>
    public sealed class EventCues : IDisposable
    {
        private const int MaxVoices = 16;

        private readonly IEventSource _events;
        private readonly EntityViews _views;
        private readonly ViewPool _pool;
        private readonly Transform _root;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private readonly List<AudioSource> _voices = new List<AudioSource>();
        private readonly List<(Transform Instance, float Left)> _effects = new List<(Transform, float)>();
        private readonly Dictionary<Cue, float> _lastPlayed = new Dictionary<Cue, float>();
        private readonly List<ParticleSystem> _particles = new List<ParticleSystem>();

        /// <param name="spatialBlend">0 plays sounds flat (2D), 1 at the entity's position (3D).</param>
        public EventCues(IEventSource events, EntityViews views, ViewPool pool, float spatialBlend = 0)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _views = views ?? throw new ArgumentNullException(nameof(views));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _root = new GameObject("EventCues").transform;
            SpatialBlend = spatialBlend;
        }

        public float SpatialBlend { get; set; }

        /// <summary>Plays nothing while true, for example while a replay fast-forwards.</summary>
        public bool Muted { get; set; }

        /// <summary>Plays <paramref name="cue"/> at the view of the entity <paramref name="at"/> returns.</summary>
        public EventCues On<TEvent>(Func<TEvent, EntityId> at, Cue cue) where TEvent : struct, IEvent
        {
            if (at == null)
                throw new ArgumentNullException(nameof(at));

            return On<TEvent>(e => PositionOf(at(e)), cue);
        }

        /// <summary>Plays <paramref name="cue"/> at a position computed from the event; null plays the sound only.</summary>
        public EventCues On<TEvent>(Func<TEvent, Vector3?> position, Cue cue) where TEvent : struct, IEvent
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));
            if (cue == null || cue.IsEmpty)
                return this;

            _subscriptions.Add(_events.Subscribe<TEvent>(e => Play(cue, position(e))));
            return this;
        }

        /// <param name="deltaTime">The game step (GameClock.Delta): 0 on pause, so effects wait with the game.</param>
        public void LateTick(float deltaTime)
        {
            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                var (instance, left) = _effects[i];
                left -= deltaTime;
                if (instance != null && left > 0)
                {
                    _effects[i] = (instance, left);
                    continue;
                }

                if (instance != null)
                    _pool.Release(instance);
                _effects.RemoveAt(i);
            }
        }

        public void Play(Cue cue, Vector3? position)
        {
            if (Muted || cue == null || cue.IsEmpty)
                return;

            var now = Time.unscaledTime;
            if (cue.MinInterval > 0 && _lastPlayed.TryGetValue(cue, out var last) && now - last < cue.MinInterval)
                return;
            _lastPlayed[cue] = now;

            if (cue.Sound != null)
                PlaySound(cue, position ?? Vector3.zero);

            if (cue.Effect != null && position is { } at)
                PlayEffect(cue, at + cue.Offset);
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();

            foreach (var (instance, _) in _effects)
                if (instance != null)
                    _pool.Release(instance);
            _effects.Clear();

            if (_root != null)
                UnityEngine.Object.Destroy(_root.gameObject);
        }

        private Vector3? PositionOf(EntityId id) =>
            _views.TryGet(id, out var view) && view != null ? view.transform.position : (Vector3?)null;

        private void PlaySound(Cue cue, Vector3 position)
        {
            var voice = FreeVoice();
            if (voice == null)
                return;

            voice.transform.position = position;
            voice.spatialBlend = SpatialBlend;
            voice.PlayOneShot(cue.Sound, cue.Volume);
        }

        private AudioSource FreeVoice()
        {
            foreach (var voice in _voices)
                if (voice != null && !voice.isPlaying)
                    return voice;

            if (_voices.Count >= MaxVoices)
                return null;

            var created = new GameObject("Voice").AddComponent<AudioSource>();
            created.transform.SetParent(_root, false);
            created.playOnAwake = false;
            _voices.Add(created);
            return created;
        }

        private void PlayEffect(Cue cue, Vector3 position)
        {
            var instance = _pool.Get(cue.Effect.transform);
            instance.position = position;
            instance.GetComponentsInChildren(_particles);
            foreach (var particles in _particles)
            {
                particles.Clear();
                particles.Play();
            }

            _effects.Add((instance, Math.Max(0.01f, cue.Lifetime)));
        }
    }
}
