using FrameworkDesign;
using ShootingEditor2D;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public abstract class CharaBase : SceneObjBase, IController
    {
        public IArchitecture GetArchitecture()
        {
            return ShootingEditor2D.Instance;
        }
    }

}
