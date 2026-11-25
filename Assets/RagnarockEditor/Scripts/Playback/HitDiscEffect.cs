using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.Playback
{
    /// <summary>
    /// Эффект удара по барабану: кружок появляется на барабане и улетает от игрока
    /// в горизонтальной плоскости (на высоте барабанов), постепенно уменьшаясь.
    ///
    /// Пул статический: при быстрой дроби объекты не создаются/не удаляются каждый удар.
    /// </summary>
    public class HitDiscEffect : MonoBehaviour
    {
        private const float Lifetime = 2.4f;
        private const float Speed = 1.4f;
        private const float SpinSpeed = 160f;
        private const float FadeInTime = 1f;
        private const float FadeOutStart = 0.58f;

        private static readonly Queue<HitDiscEffect> Pool = new Queue<HitDiscEffect>();

        private Vector3 _direction;
        private Vector3 _baseScale = Vector3.one;
        private MeshRenderer _renderer;
        private Color _color = Color.white;
        private float _elapsed;
        private float _scale = 1f;
        private float _spin;

        /// <summary>
        /// Спавнит кружок в плоскости барабана. Направление — от игрока вглубь сцены
        /// (то есть «вперёд по палубе»).
        /// </summary>
        public static void Spawn(Vector3 position, int drumIndex, float velocity)
        {
            var effect = Get();
            if (effect == null)
                return;

            // Все кружки летят параллельно — по направлению палубы, а не радиально от камеры
            Vector3 direction = DeckOrientation.Forward;
            effect._direction = direction;
            effect._elapsed = 0f;
            effect._spin = 0f;
            effect._scale = Mathf.Lerp(0.85f, 1.15f, Mathf.InverseLerp(0.6f, 7f, velocity));
            effect._color = DiscVisual.DrumColor(drumIndex);

            DiscVisual.ApplyColor(effect._renderer, effect._color, 0f);

            effect.transform.position = position;
            effect.transform.rotation = Quaternion.identity;
            effect.transform.localScale = effect._baseScale * effect._scale;
            effect.gameObject.SetActive(true);
        }

        private static HitDiscEffect Get()
        {
            while (Pool.Count > 0)
            {
                var pooled = Pool.Dequeue();
                if (pooled != null)
                    return pooled;
            }

            var go = new GameObject("HitDisc");
            var renderer = DiscVisual.Create(go, 0);
            go.transform.localScale = DiscVisual.ScaleForWorldSize(
                DiscVisual.Radius, DiscVisual.Thickness, 1f);

            var effect = go.AddComponent<HitDiscEffect>();
            effect._renderer = renderer;
            effect._baseScale = go.transform.localScale;
            return effect;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            _elapsed += dt;
            float t = Mathf.Clamp01(_elapsed / Lifetime);

            // летит прочь от игрока, чуть замедляясь
            transform.position += _direction * (Speed * (1f - 0.35f * t) * dt);

            _spin += SpinSpeed * dt;
            transform.rotation = Quaternion.Euler(0f, _spin, 0f);

            // Размер постоянный. Появление на границе и исчезновение — только прозрачностью.
            transform.localScale = _baseScale * _scale;

            float alpha;
            if (t < FadeInTime)
                alpha = t / FadeInTime;
            else if (t > FadeOutStart)
                alpha = Mathf.Clamp01((1f - t) / (1f - FadeOutStart));
            else
                alpha = 1f;

            DiscVisual.ApplyColor(_renderer, _color, alpha);

            if (t >= 1f)
            {
                gameObject.SetActive(false);
                Pool.Enqueue(this);
            }
        }
    }
}
