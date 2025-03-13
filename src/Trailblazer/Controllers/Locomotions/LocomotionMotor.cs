namespace Trailblazer.Controllers.Locomotions
{
    [System.Serializable]
    public class LocomotionMotor
    {
        /// <summary>
        /// Does this script currently respond to input?
        /// </summary>
        public bool IsInControl = true;
   
        #region Locomotions

        public MoveLocomotion Move = new();

        // controls ground state and maintains state while in air
        public PlatformLocomotion Platform = new();

        // controls air state when a jump was processed successfully
        public JumpLocomotion Jump = new();

        // controls air state when any downward momentum detected or is grounded and sliding
        public FallLocomotion Fall = new();

        // controls ground state velocity when is sliding 
        public SlideLocomotion Slide = new();

        // controls water state
        public SwimLocomotion Swim = new();

        #endregion
      
        public void SyncState(LocomotionMotor other)
        {
            IsInControl = other.IsInControl;

            if (Platform.IsEnabled)
                Platform.SyncState(other.Platform);

            if (Jump.IsEnabled)
                Jump.SyncState(other.Jump);

            if (Fall.IsEnabled)
                Fall.SyncState(other.Fall);

            if (Swim.IsEnabled)
                Swim.SyncState(other.Swim);

            if (Slide.IsEnabled)
                Slide.SyncState(other.Slide);
        }

        public void ClearStateAll()
        {
            if (Platform.IsEnabled)
                Platform.ClearState();

            if (Jump.IsEnabled)
                Jump.ClearState();

            if (Fall.IsEnabled)
                Fall.ClearState();

            if (Swim.IsEnabled)
                Swim.ClearState();

            if (Slide.IsEnabled)
                Slide.ClearState();
        }
    }
}
