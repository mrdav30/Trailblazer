using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    public struct GroupBehaviorWeights
    {
        public Fixed64 Separation;
        public Fixed64 Alignment;
        public Fixed64 Cohesion;
    }

    public static class NavigatorSteering
    {
        private static readonly Fixed64 DefaultSearchRange = Fixed64.One * 10;

        private static readonly Fixed64 DefaultAvoidPadding = Fixed64.One * 3;

        private static readonly GroupBehaviorWeights DefaultBehaviorWeights = new()
        {
            Separation = (Fixed64)2,
            Alignment = Fixed64.Half,
            Cohesion = (Fixed64)0.2f
        };

        public static Vector3d ComputeGroupSteering(
            Vector3d from, 
            Fixed64 speed, 
            Fixed64? padding = null,
            GroupBehaviorWeights? weights = null)
        {
            int neighboursCount = 0;
            Fixed64 paddingRadius = padding ?? DefaultAvoidPadding;
            GroupBehaviorWeights groupWeights = weights ?? DefaultBehaviorWeights;

            Vector3d totalForce = Vector3d.Zero;
            Vector3d averageHeading = Vector3d.Zero;
            //  Sum up the position of our neighbours
            Vector3d centerOfMass = Vector3d.Zero;

            foreach (INodeOccupant entity in ScanManager.ScanRadius(from, paddingRadius))
            {
                if (entity is not IAvoidanceBody other)
                    continue;

                Vector3d distance = from - entity.WorldPosition;
                distance.Normalize(out Fixed64 distanceMagnitude);

                // Move away from neighbor if we are too close to
                totalForce += (distance * (Fixed64.One - (distanceMagnitude / paddingRadius)));

                //  Move closer to entities we are near but not close enough to
                centerOfMass += entity.WorldPosition;

                //  Change our direction to be closer to our neighbours that are within the max distance and are moving
                if (other.Velocity != Vector3d.Zero)
                    averageHeading += other.Velocity.Normal;

                neighboursCount++;
            }

            if (neighboursCount <= 0)
                return Vector3d.Zero;

            //  Separation calculates a force to move away from all of our neighbours. 
            //  We do this by calculating a force from them to us and scaling it so the force is greater the closer they are.
            Vector3d seperation = totalForce * (speed / (neighboursCount * Fixed64.One));

            //  Cohesion and Alignment are for when other agents going to a similar location as us.
            //  Otherwise we’ll get caught up when other agents move past.

            Fixed64 invNeighborCount = Fixed64.One / neighboursCount;

            //  Alignment calculates a force so that our direction is closer to our neighbours.
            //  It does this similar to cohesion, but by summing up the direction vectors (normalised velocities) of ourself 
            //  and our neighbours and working out the average direction.
            //  Divide by amount of neighbors to get the average heading
            Vector3d alignment = averageHeading * invNeighborCount;

            //  Cohesion calculates a force that will bring us closer to our neighbours, so we move together as a group rather than individually.
            //  Cohesion calculates the average position of our neighbours and ourself, and steers us towards it
            //  seek this position
            Vector3d cohesion = SteeringBehaviorSeek(from, centerOfMass * invNeighborCount, speed);

            //  Combine them to come up with a total force to apply, decreasing the effect of cohesion
            return (seperation * groupWeights.Separation) + (alignment * groupWeights.Alignment) + (cohesion * groupWeights.Cohesion);
        }

        private static Vector3d SteeringBehaviorSeek(Vector3d from, Vector3d destination, Fixed64 speed)
        {
            if (destination == from)
                return Vector3d.Zero;

            // Desired change of location
            Vector3d desired = destination - from;

            desired.Normalize(out Fixed64 desiredSpeed);
            //Desired velocity (move there at maximum speed)
            return desiredSpeed > Fixed64.Zero ? desired * (speed / desiredSpeed) : Vector3d.Zero;
        }

        public static Vector3d CalculateAvoidanceForce(
            IAvoidanceBody body, 
            Fixed64? range = null, 
            Func<IAvoidanceBody, bool> filter = null)
        {
            if (body.Velocity.SqrMagnitude <= Fixed64.Zero)
                return Vector3d.Zero;

            IAvoidanceBody closest = null;
            Fixed64 avoidRadius = range ?? DefaultSearchRange;
            Fixed64 minAvoidanceDistance = avoidRadius;

            foreach (var entity in ScanManager.ScanRadius(body.Position, avoidRadius))
            {
                if (entity is not IAvoidanceBody other)
                    continue;

                if (filter != null && !filter(other))
                    continue;

                Vector3d toOther = other.Position - body.Position;
                toOther.Normalize(out Fixed64 distance);

                if (distance < minAvoidanceDistance)
                {
                    closest = other;
                    minAvoidanceDistance = distance;
                }
            }

            if (closest == null)
                return Vector3d.Zero;

            // Direction from agent to the other
            Vector3d avoidanceDir = closest.Position - body.Position;

            if (closest.IsAvoidingLeft)
                body.IsAvoidingLeft = true;
            else
            {
                // Left/right test using 2D determinant
                Fixed64 dot = body.Velocity.x * -avoidanceDir.z + body.Velocity.z * avoidanceDir.x;
                body.IsAvoidingLeft = dot > Fixed64.Zero;
            }

            // Rotate vector by ±90° in XZ
            Vector3d perp = body.IsAvoidingLeft
                ? new Vector3d(-avoidanceDir.z, Fixed64.Zero, avoidanceDir.x)
                : new Vector3d(avoidanceDir.z, Fixed64.Zero, -avoidanceDir.x);

            perp.Normalize();

            // Adjust force based on combined radius
            Fixed64 combinedRadius = body.Radius + closest.Radius;

            Vector3d force = perp * (combinedRadius / minAvoidanceDistance);
            return force;
        }
    }
}
