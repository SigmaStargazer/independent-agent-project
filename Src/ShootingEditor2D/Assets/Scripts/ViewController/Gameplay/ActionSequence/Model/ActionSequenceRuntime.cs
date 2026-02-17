using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class ActionSequenceRuntime
    {
        public List<DeviceBase> sceneObjsSnap = new List<DeviceBase>();

        public void AddSceneObj(DeviceBase sceneObj)
        {
            sceneObjsSnap.Add(sceneObj);
        }
    }
}


