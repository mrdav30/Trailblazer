using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    [System.Serializable]
    public class LocomotionData
    {
        /// <summary>
        /// Does this script currently respond to input?
        /// </summary>
        public bool CanControl = true;
   
        #region Locomotions

        public MoveLocomotion Move = new();

        public PlatformLocomotion Platform = new();

        public JumpLocomotion Jump = new();

        public FallLocomotion Fall = new();

        public SlideLocomotion Slide = new();

        public SwimLocomotion Swim = new();

        #endregion
      
        public void SyncState(LocomotionData other)
        {
            CanControl = other.CanControl;

            if (Platform.IsEnabled)
                Platform.SyncState(other.Platform);

            if (Jump.IsEnabled)
                Jump.SyncState(other.Jump);

            if (Fall.IsEnabled)
                Fall.SyncState(other.Fall);

            if (Swim.IsEnabled)
                Swim.SyncState(other.Swim);
        }

        public void ClearStateAll()
        {
            Platform.ClearState();
            Jump.ClearState();
            Fall.ClearState();
            Swim.ClearState();
        }
    }
}
