using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderCore
{
    public class WorkersPanel : MonoBehaviour
    {
        [Tooltip("used to display workers(10 icons, 8 worker slots, 5 workers active >> last 2 icons will be deactivated, first 5 will have their image changed to the worker icon)")]
        public Image[] Icons;

        public void SetWorkers(IEnumerable<Worker> workers)
        {
            int count = workers.Count();
            for (int i = 0; i < Icons.Length; i++)
            {
                if (i >= count)
                {
                    Icons[i].gameObject.SetActive(false);
                }
                else
                {
                    Icons[i].gameObject.SetActive(true);

                    var worker = workers.ElementAt(i);
                    if (worker == null)
                    {
                        Icons[i].sprite = null;
                        Icons[i].color = Color.clear;
                    }
                    else
                    {
                        Icons[i].sprite = worker.Icon;
                        Icons[i].color = Color.white;
                    }
                }
            }
        }
    }
}
