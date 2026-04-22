using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class GameTimer : MonoBehaviour
{
    [Header("Timer Settings")]
    [Tooltip("Total time in seconds. 10 minutes = 600 seconds.")]
    public float timeRemaining = 600f; 
    
    [Tooltip("Check this if you want the timer to start on awake.")]
    public bool timerIsRunning = true;

    [Header("HUD Settings")]
    [Tooltip("Check this to make the timer follow the main camera's movement.")]
    public bool followCamera = true;
    [Tooltip("The offset from the camera's center. Default places it slightly below and 1 meter ahead.")]
    public Vector3 cameraOffset = new Vector3(0f, -0.2f, 1.0f);
    [Tooltip("Smoothness of the camera follow. Set to 0 for instant, or higher for smoother follow.")]
    public float smoothSpeed = 10f;

    private TextMeshProUGUI timeText;

    private void Awake()
    {
        // Automatically get the TextMeshPro component attached to this exact same UI object
        timeText = GetComponent<TextMeshProUGUI>();
    }

    private void Start()
    {
        // Starts the timer automatically 
        // timerIsRunning is set in inspector, keeping it as is unless it needs a reset
    }

    private void Update()
    {
        if (timerIsRunning)
        {
            if (timeRemaining > 0)
            {
                timeRemaining -= Time.deltaTime;
                DisplayTime(timeRemaining);
            }
            else
            {
                Debug.Log("Time has run out!");
                timeRemaining = 0;
                timerIsRunning = false;
                DisplayTime(timeRemaining);
                
                // You can add logic here to trigger a game over event
            }
        }
    }

    private void LateUpdate()
    {
        if (followCamera && Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            Vector3 targetPosition = cam.position + cam.TransformDirection(cameraOffset);
            Quaternion targetRotation = cam.rotation;

            if (smoothSpeed > 0f)
            {
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothSpeed);
                transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * smoothSpeed);
            }
            else
            {
                transform.position = targetPosition;
                transform.rotation = targetRotation;
            }
        }
    }

    private void DisplayTime(float timeToDisplay)
    {
        // Ensure we don't display negative time
        if(timeToDisplay < 0) 
        {
            timeToDisplay = 0;
        }

        // Calculate minutes and seconds
        float minutes = Mathf.FloorToInt(timeToDisplay / 60); 
        float seconds = Mathf.FloorToInt(timeToDisplay % 60);

        // Format the string to look like a digital clock (e.g. 10:00)
        timeText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }
    
    // Call this method from other scripts to stop the timer (e.g. when the door opens)
    public void StopTimer()
    {
        timerIsRunning = false;
    }
}
