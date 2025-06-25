using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// implementation of <see cref="IHighlightManager"/> that instantiates sprites
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_sprite_highlight_manager.html")]
    public class SpriteHighlightManager : MonoBehaviour, IHighlightManager
    {
        [Tooltip("prefab that is instantiated for a single highlighting point")]
        public SpriteRenderer Prefab;
        [Tooltip("color set in the sprite renderer for valid points")]
        public Color ValidColor = Color.green;
        [Tooltip("color set in the sprite renderer for invalid points")]
        public Color InvalidColor = Color.red;
        [Tooltip("color set in the sprite renderer for info points")]
        public Color InfoColor = Color.blue;

        private IGridPositions _gridPositions;
        private IGridHeights _gridHeights;

        private Dictionary<Vector2Int, SpriteRenderer> _used = new Dictionary<Vector2Int, SpriteRenderer>();
        private Queue<SpriteRenderer> _pool = new Queue<SpriteRenderer>();

        protected virtual void Awake()
        {
            Dependencies.Register<IHighlightManager>(this);
        }

        private void Start()
        {
            _gridPositions = Dependencies.Get<IGridPositions>();
            _gridHeights = Dependencies.GetOptional<IGridHeights>();
        }

        public void Highlight(IEnumerable<Vector2Int> points, bool valid) => Highlight(points, valid ? ValidColor : InvalidColor);
        public void Highlight(IEnumerable<Vector2Int> points, HighlightType type) => Highlight(points, getColor(type));
        public void Highlight(IEnumerable<Vector2Int> points, Color color)
        {
            foreach (var position in points)
            {
                Highlight(position, color);
            }
        }

        public void Highlight(Vector2Int point, bool isValid) => Highlight(point, isValid ? ValidColor : InvalidColor);
        public void Highlight(Vector2Int point, HighlightType type) => Highlight(point, getColor(type));
        public void Highlight(Vector2Int point, Color color)
        {
            if (!_used.ContainsKey(point))
            {
                SpriteRenderer spriteRenderer;

                if (_pool.Count == 0)
                    spriteRenderer = Instantiate(Prefab, transform);
                else
                    spriteRenderer = _pool.Dequeue();

                spriteRenderer.gameObject.SetActive(true);
                spriteRenderer.transform.position = _gridPositions.GetWorldCenterPosition(point);

                if (_gridHeights != null)
                    _gridHeights.ApplyHeight(spriteRenderer.transform, PathType.Any);

                _used.Add(point, spriteRenderer);
            }

            _used[point].color = color;
        }

        public void Clear()
        {
            _used.Values.ForEach(u =>
            {
                u.gameObject.SetActive(false);
                _pool.Enqueue(u);
            });
            _used.Clear();
        }

        private Color getColor(HighlightType type)
        {
            switch (type)
            {
                case HighlightType.Valid:
                    return ValidColor;
                case HighlightType.Invalid:
                    return InvalidColor;
                case HighlightType.Info:
                    return InfoColor;
                default:
                    return Color.red;
            }
        }
    }
}