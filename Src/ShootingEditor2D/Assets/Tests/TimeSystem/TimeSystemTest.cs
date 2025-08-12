using UnityEngine;

namespace ShootingEditor2D.Tests
{
    public class TimeSystemTest : MonoBehaviour
    {
        // Start is called before the first frame update
        void Start()
        {
            ITimeSystem timeSystem = GetComponent<ITimeSystem>();
            Debug.Log(timeSystem.CurSeconds);
            timeSystem.AddDelayTask(3, () =>
            {
                Debug.Log(timeSystem.CurSeconds);
            });
        }
    }
}

