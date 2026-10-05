using UnityEngine;

namespace PoseKart.Vehicle
{
    // Solid from inside. Outside karts may re-enter; this never teleports a kart.
    public sealed class TrackReturnBoundary : MonoBehaviour
    {
        [SerializeField] private Vector3 outward;
        private Collider wall;
        private CarController[] cars;
        private Collider[][] bodies;
        private bool[] returning;
        public void Configure(Vector3 direction){outward=direction.normalized;}
        private void Start()
        {
            wall=GetComponent<Collider>();wall.isTrigger=false;
            cars=FindObjectsByType<CarController>(FindObjectsSortMode.None);
            bodies=new Collider[cars.Length][];returning=new bool[cars.Length];
            for(int i=0;i<cars.Length;i++)bodies[i]=cars[i].GetComponentsInChildren<Collider>();
        }
        private void FixedUpdate()
        {
            for(int i=0;i<cars.Length;i++)
            {
                if(cars[i]==null)continue;
                float side=Vector3.Dot(cars[i].transform.position-transform.position,outward);
                bool allow=returning[i]?side> -2f:side>1.2f;
                if(allow==returning[i])continue;
                returning[i]=allow;
                foreach(Collider body in bodies[i])if(body!=null&&!body.isTrigger)Physics.IgnoreCollision(wall,body,allow);
            }
        }
    }
}
