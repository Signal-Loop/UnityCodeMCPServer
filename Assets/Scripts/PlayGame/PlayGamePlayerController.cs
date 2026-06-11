using UnityEngine;
using UnityEngine.InputSystem;

public class PlayGamePlayerController : MonoBehaviour
{
    [SerializeField] private InputActionReference player1_up;
    [SerializeField] private InputActionReference player1_down;
    [SerializeField] private float move_speed = 4f;

    private void OnEnable()
    {
        player1_up?.action?.Enable();
        player1_down?.action?.Enable();
    }

    private void OnDisable()
    {
        player1_up?.action?.Disable();
        player1_down?.action?.Disable();
    }

    private void Update()
    {
        float vertical_input = 0f;

        if (player1_up != null && player1_up.action != null && player1_up.action.IsPressed())
        {
            vertical_input += 1f;
        }

        if (player1_down != null && player1_down.action != null && player1_down.action.IsPressed())
        {
            vertical_input -= 1f;
        }

        if (Mathf.Approximately(vertical_input, 0f))
        {
            return;
        }

        transform.Translate(Vector3.up * (vertical_input * move_speed * Time.deltaTime), Space.World);
    }
}
