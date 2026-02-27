using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class MouseClickInteractor2D : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Debug.Log("Click");
                Vector2 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero);

                if (hit.collider == null)
                {
                    Debug.Log("Click Empty");
                    return;
                }
                    

                var device = hit.collider.GetComponent<DeviceBase>();
                if (device == null) return;

                if (!device.IsClickable)
                {
                    Debug.Log($"{device.Name} ²»¿Éµã»÷£¡");
                    return;
                }

                device.OnClick();
            }
        }
    }
}