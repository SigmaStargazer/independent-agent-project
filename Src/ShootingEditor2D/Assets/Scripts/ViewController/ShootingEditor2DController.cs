using FrameworkDesign;
using UnityEngine;

namespace ShootingEditor2D
{
    public class ShootingEditor2DController : MonoBehaviour, IController
    {
        public IArchitecture GetArchitecture()
        {
            return ShootingEditor2D.Instance;
        }
    }

}
