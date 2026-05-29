using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FrameworkDesign;

namespace ShootingEditor2D
{
    public class BulletPickItem : ShootingEditor2DController
    {
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.CompareTag("Player"))
            {
                this.SendCommand<AddBulletCommand>();
            }
        }
    }

}