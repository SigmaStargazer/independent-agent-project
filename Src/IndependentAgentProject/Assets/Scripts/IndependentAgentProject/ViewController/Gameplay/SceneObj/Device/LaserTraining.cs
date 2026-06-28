using FrameworkDesign;
using IndependentAgentProject;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class LaserTraining : MonoBehaviour
    {
        private void OnTriggerEnter2D(Collider2D collision)
        {
            Debug.Log("Laser Trigger");
            PlayerBase player = collision.GetComponent<PlayerBase>();

            if (player != null)
            {
                //player.ReturnToCheckPoint(this);
            }
        }
    }
}

