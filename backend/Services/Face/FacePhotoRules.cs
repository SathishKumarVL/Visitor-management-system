namespace Tiaano.Vms.Api.Services.Face;

/// <summary>
/// Visitor photo capture must contain exactly one detectable face.
/// Messages are shared by API responses and the registration camera UX.
/// </summary>
public static class FacePhotoRules
{
    public const string NoFaceMessage =
        "No face detected. Please position one person in front of the camera.";

    public const string MultipleFacesMessage =
        "Multiple faces detected. Please ensure only one person is in the frame.";

    public const string DetectorUnavailableMessage =
        "Face detection is required to save a visitor photo, but it is not available on this server.";

    public const string ErrorCodeNoFace = "NO_FACE_DETECTED";
    public const string ErrorCodeMultipleFaces = "MULTIPLE_FACES_DETECTED";
    public const string ErrorCodeDetectorUnavailable = "FACE_DETECTOR_UNAVAILABLE";

    public static string MessageForCount(int faceCount) => faceCount switch
    {
        0 => NoFaceMessage,
        1 => "Ready to capture",
        _ => MultipleFacesMessage
    };

    public static string? ErrorCodeForCount(int faceCount) => faceCount switch
    {
        0 => ErrorCodeNoFace,
        1 => null,
        _ => ErrorCodeMultipleFaces
    };
}
