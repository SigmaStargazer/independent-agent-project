using FrameworkDesign;
using IndependentAgentProject;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class LaserGrid : DeviceBase
    {
        public override string Name => "激光网";
        public override string Desc => "接触后会直接死亡";
        public override bool IsInteractable => false;

        private void OnTriggerEnter2D(Collider2D collision)
        {
            PlayerBase player = collision.GetComponent<PlayerBase>();

            if (player != null)
            {
                player.Die();
            }
        }
    }
}

