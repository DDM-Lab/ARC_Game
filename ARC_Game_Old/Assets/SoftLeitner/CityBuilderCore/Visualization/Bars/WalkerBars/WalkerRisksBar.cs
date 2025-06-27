using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// highlights risk walkers by displaying the risk icon above them
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_risks_bar.html")]
    public class WalkerRisksBar : WalkerValueBar
    {
        [Tooltip("prefab for one icon instance")]
        public SpriteRenderer Prefab;

        private IMainCamera _mainCamera;
        private Risk _risk;
        private RiskCategory _riskCategory;
        private SpriteRenderer _spriteRenderer;

        private void Start()
        {
            _mainCamera = Dependencies.Get<IMainCamera>();

            setBar();
        }

        private void Update()
        {
            setBar();
        }

        public override void Initialize(Walker walker, IWalkerValue value)
        {
            base.Initialize(walker, value);

            _risk = value as Risk;
            _riskCategory = value as RiskCategory;
        }

        private void setBar()
        {
            transform.forward = _mainCamera.Camera.transform.forward;

            if ((_risk != null && _risk.HasValue(Walker)) || (_riskCategory != null && _riskCategory.HasValue(Walker)))
            {
                if (_spriteRenderer == null)
                {
                    _spriteRenderer = Instantiate(Prefab, transform);
                    _spriteRenderer.sprite = ((RiskWalker)Walker).Risk.Icon;
                    _spriteRenderer.transform.localPosition = Vector3.zero;
                }
            }
            else
            {
                if (_spriteRenderer != null)
                {
                    Destroy(_spriteRenderer.gameObject);
                    _spriteRenderer = null;
                }
            }
        }
    }
}