using FrameworkDesign;
using IndependentAgentProject;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class Laser : MonoBehaviour
    {
        private void OnTriggerEnter2D(Collider2D collision)
        {
            Debug.Log("Laser Trigger");
            PlayerBase player = collision.GetComponent<PlayerBase>();

            if (player != null)
            {
                player.Die();
            }
        }
    }
}

