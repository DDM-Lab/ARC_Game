using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// dialog for employment stuff<br/>
    /// can be used to configure employment priorities
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_employment_dialog.html")]
    public class EmploymentDialog : DialogBase
    {
        [Tooltip("prefab for employment priorities")]
        public EmploymentPriorityPanel Prefab;
        [Tooltip("parent for the employment priority panels")]
        public Transform Content;

        private List<EmploymentPriorityPanel> _panels;

        private List<EmploymentGroup> _employmentGroups;
        private IEmploymentManager _employmentManager;

        public override void Activate()
        {
            base.Activate();

            if (_employmentGroups == null)
            {
                _employmentGroups = Dependencies.Get<IObjectSet<EmploymentGroup>>().Objects.ToList();
                _employmentManager = Dependencies.Get<IEmploymentManager>();
            }

            _panels = new List<EmploymentPriorityPanel>();

            var orderedGroups = _employmentGroups.OrderByDescending(g => _employmentManager.GetPriority(g)).ToArray();

            for (int i = 0; i < orderedGroups.Length; i++)
            {
                var group = orderedGroups[i];
                var panel = Instantiate(Prefab, Content);
                panel.Initialize(group);
                panel.SetVisibilities(i == 0, i == orderedGroups.Length - 1);
                panel.Uped += moveUp;
                panel.Downed += moveDown;
                _panels.Add(panel);
            }
        }

        public override void Deactivate()
        {
            base.Deactivate();

            _panels.ForEach(p => Destroy(p.gameObject));
            _panels = null;
        }

        private void moveUp(EmploymentPriorityPanel panel) => switchGroups(_panels[_panels.IndexOf(panel) - 1], panel);
        private void moveDown(EmploymentPriorityPanel panel) => switchGroups(panel, _panels[_panels.IndexOf(panel) + 1]);

        private void switchGroups(EmploymentPriorityPanel first, EmploymentPriorityPanel second)
        {
            var firstIndex = _panels.IndexOf(first);

            var priority = _employmentManager.GetPriority(first.EmploymentGroup);
            var otherPriority = _employmentManager.GetPriority(second.EmploymentGroup);

            _employmentManager.SetPriority(first.EmploymentGroup, otherPriority);
            _employmentManager.SetPriority(second.EmploymentGroup, priority);

            _panels.Remove(second);
            _panels.Insert(firstIndex, second);

            second.transform.SetSiblingIndex(second.transform.GetSiblingIndex() - 1);

            setVisibilities(first);
            setVisibilities(second);
        }

        private void setVisibilities(EmploymentPriorityPanel panel)
        {
            var index = _panels.IndexOf(panel);
            panel.SetVisibilities(index == 0, index == _panels.Count - 1);
        }
    }
}