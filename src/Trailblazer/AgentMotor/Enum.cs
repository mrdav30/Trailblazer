namespace Trailblazer.AgentMotor
{
    public enum MovementTransferState
    {
        /// <summary>
        /// The driver is not affected by velocity of the platform at all.
        /// </summary>
        None = -1,
        /// <summary>
        /// Driver gets its initial velocity from the platform, then gradualy comes to a stop.
        /// </summary>
        InitTransfer = 0,
        /// <summary>
        /// Driver gets its initial velocity from the platform, and keeps that velocity until landing.
        /// </summary>
        PermaTransfer = 1,
        /// <summary>
        /// Driver is relative to the movement of the last touched platform and will move together with that platform.
        /// </summary>
        PermaLocked = 2
    }

    // check + jump input for complete state
    public enum MovementInput
    {
        None = -1,
        Walk = 0,
        Jog = 1,
        Run = 2
    }

    // check + jump input for complete state
    public enum SpeedState
    {
        None = -1,
        Normal = 0,
        Fast = 1,
        Faster = 2,
    }
}
