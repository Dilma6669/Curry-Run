using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class PlayerColliderDebug : MonoBehaviour
    {
        
        private void OnTriggerStay2D(Collider2D other)
        {
            
            //Debug.Log($"fuck OnTriggerStay2D other.name: {other.name}");
            
        }
        
        private void OnCollisionStay2D(Collision2D other)
        {
            //Debug.Log($"fuck OnCollisionStay other.name: {other.gameObject.name}");
            
        }
    }
}