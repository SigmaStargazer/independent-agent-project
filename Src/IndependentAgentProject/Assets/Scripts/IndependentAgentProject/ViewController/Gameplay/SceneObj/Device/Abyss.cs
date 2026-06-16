using FrameworkDesign;
using IndependentAgentProject;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class Abyss : SceneObjBase
    {
        public override string Name => "深渊";
        public override string Desc => "深不见底。一旦掉下去，后果不堪设想。";


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

