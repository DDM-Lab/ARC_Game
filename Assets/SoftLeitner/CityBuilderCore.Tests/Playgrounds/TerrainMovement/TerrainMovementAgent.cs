using UnityEngine;
using UnityEngine.AI;

namespace CityBuilderCore.Tests
{
    public class TerrainMovementAgent : MonoBehaviour
    {
        public NavMeshAgent Agent;
        public Transform Destination;

        // Start is called before the first frame update
        void Start()
        {
            Agent.SetDestination(Destination.position);
        }

        // Update is called once per frame
        void Update()
        {

        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;

            for (int i = 1; i < Agent.path.corners.Length - 1; i++)
            {
                Gizmos.DrawLine(Agent.path.corners[i - 1], Agent.path.corners[i]);
            }
        }
    }
}