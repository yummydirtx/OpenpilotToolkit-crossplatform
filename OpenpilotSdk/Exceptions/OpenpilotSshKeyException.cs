namespace OpenpilotSdk.Exceptions;

public enum OpenpilotSshKeyErrorKind
{
    PassphraseRequired,
    InvalidPassphrase,
    InvalidPrivateKey
}

public sealed class OpenpilotSshKeyException : InvalidOperationException
{
    public OpenpilotSshKeyException(
        OpenpilotSshKeyErrorKind errorKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorKind = errorKind;
    }

    public OpenpilotSshKeyErrorKind ErrorKind { get; }

    public bool CanRetryWithPassphrase =>
        ErrorKind is OpenpilotSshKeyErrorKind.PassphraseRequired or OpenpilotSshKeyErrorKind.InvalidPassphrase;
}
