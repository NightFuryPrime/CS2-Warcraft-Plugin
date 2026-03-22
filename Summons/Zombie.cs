using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Drawing;
using WarcraftPlugin.Diagnostics;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Summons
{
    internal class Zombie : IDisposable
    {
        private const int _interestMax = 6;
        private int InterestScore = _interestMax;
        private readonly int _radius = 50;
        private readonly double _leapCooldown = 1;
        private readonly int _damage = 10;
        private readonly int _maxHealth = 100;

        internal int FavouritePosition { get; set; } = 1;
        internal CChicken Entity { get; set; }
        internal CCSPlayerController Owner { get; }
        internal bool IsFollowingLeader { get; private set; }
        internal CCSPlayerController Target { get; set; }
        internal double LastLeapTick { get; private set; } = 0;
        internal double LastAttackTick { get; private set; } = 0;

        internal Zombie(CCSPlayerController owner)
        {
            Owner = owner;
            Entity = Utilities.CreateEntityByName<CChicken>("chicken");
            if (Entity == null || !Entity.IsValid)
            {
                PersistentLogger.Error(nameof(Zombie), "Failed to create zombie chicken entity.");
                return;
            }

            Entity.Teleport(Owner.CalculatePositionInFront(new Vector(RandomProvider.Next(200), RandomProvider.Next(200), 5)), new QAngle(), new Vector());
            Entity.DispatchSpawn();
            Entity.SetColor(Color.GreenYellow);
            PersistentLogger.Breadcrumb(nameof(Zombie), "Applying zombie entity visual setup.", throttleMs: 0);
            var skeleton = Entity.CBodyComponent?.SceneNode?.GetSkeletonInstance();
            if (skeleton != null)
            {
                skeleton.Scale = 2f;
            }
            Entity.Health = _maxHealth;

            Warcraft.SpawnParticle(Entity.AbsOrigin.Clone().Add(z: 5), "particles/entity/env_explosion/test_particle_composite_dark_outline_smoke.vpcf");

            if (Owner.PlayerPawn != null && Owner.PlayerPawn.IsValid)
            {
                Entity.OwnerEntity.Raw = Owner.PlayerPawn.Raw;
            }
            FollowLeader();
        }

        internal void Update()
        {
            if (Entity == null || !Entity.IsValid) return;
            if (!Owner.PawnIsAlive)
            {
                Kill();
                return; // Early return to prevent further execution
            }

            if(InterestScore <= 0)
            {
                FollowLeader();
            }

            if (Target != null && Target.PawnIsAlive)
            {
                if (LastLeapTick == 0 || LastLeapTick + _leapCooldown + RandomProvider.NextDouble() < Server.TickedTime)
                {
                    AttackLeap();
                }
            }
            else if(IsFollowingLeader)
            {
                var ownerPawn = Owner.PlayerPawn?.Value;
                if (ownerPawn == null) return;

                //Ensure chicken is not stuck
                float chickenDistanceToPlayer = (ownerPawn.AbsOrigin - Entity.AbsOrigin).Length();
                if (chickenDistanceToPlayer > 500)
                {
                    var chickenResetPoint = Owner.CalculatePositionInFront(new Vector(RandomProvider.Next(100), RandomProvider.Next(100), 5));
                    Entity.AbsOrigin.X = chickenResetPoint.X;
                    Entity.AbsOrigin.Y = chickenResetPoint.Y;
                    Entity.AbsOrigin.Z = ownerPawn.AbsOrigin.Z+5;
                    Warcraft.SpawnParticle(Entity.AbsOrigin.Clone().Add(z: -50), "particles/entity/env_explosion/test_particle_composite_dark_outline_smoke.vpcf");
                    return;
                }
                Vector velocity = CircularGetVelocityToPosition(ownerPawn.AbsOrigin, Entity.AbsOrigin);

                //Give them a boost so their little chicken feet can keep up
                Entity.AbsVelocity.X = Math.Clamp(velocity.X, -300, 300);
                Entity.AbsVelocity.Y = Math.Clamp(velocity.Y, -300, 300);
                Entity.AbsVelocity.Z = 10;
            }
        }

        private Vector CircularGetVelocityToPosition(Vector circleTarget, Vector zombie, int radius = 50)
        {
            // Calculate the angle in radians (map input 1-100 to 0-2π)
            double angle = (FavouritePosition - 1) / 99.0 * 2 * Math.PI;

            // Calculate x and y offsets based on the angle and radius
            float offsetX = (float)(_radius * Math.Cos(angle));
            float offsetY = (float)(_radius * Math.Sin(angle));

            // Add these offsets to the owner's position
            Vector targetPosition = circleTarget.Clone()
                .Add(x: offsetX, y: offsetY);

            // Calculate the travel velocity
            Vector velocity = Warcraft.CalculateTravelVelocity(zombie, targetPosition, 1);
            return velocity;
        }

        private void AttackLeap()
        {
            if (Target == null) return;
            var targetPawn = Target.PlayerPawn?.Value;
            if (targetPawn == null || !targetPawn.IsValid) return;

            LastLeapTick = Server.TickedTime;
            Attack();

            //Leap logic
            Vector velocity = Warcraft.CalculateTravelVelocity(Entity.AbsOrigin, targetPawn.AbsOrigin, 1);

            Entity.AbsVelocity.Z = 400;
            Entity.AbsVelocity.X = Math.Clamp(velocity.X, -1000, 1000);
            Entity.AbsVelocity.Y = Math.Clamp(velocity.Y, -1000, 1000);
        }

        private void Attack()
        {
            if (Target == null) return;
            var targetPawn = Target.PlayerPawn?.Value;
            if (targetPawn == null || !targetPawn.IsValid) return;

            var playerCollison = targetPawn.Collision.ToBox(targetPawn.AbsOrigin.Clone().Add(z: -60));

            //Check if zombie is inside targets collision box
            if (playerCollison.Contains(Entity.AbsOrigin))
            {
                //dodamage to target
                Target.TakeDamage(_damage, Owner, KillFeedIcon.fists);
                Entity.EmitSound("SprayCan.ShakeGhost", volume: 0.1f);
                InterestScore = _interestMax;
            }
            else
            {
                InterestScore--;
            }
        }

        internal void Kill()
        {
            Entity.RemoveIfValid();
        }

        internal void SetEnemy(CCSPlayerController enemy)
        {
            if (!enemy.IsAlive()) return;
            var enemyPawn = enemy.PlayerPawn?.Value;
            if (enemyPawn == null || !enemyPawn.IsValid) return;

            if (Target != null && Target.PawnIsAlive)
            {
                return;
            }

            if (Target != null && Target == enemy) { return; }
            IsFollowingLeader = false;
            InterestScore = _interestMax;
            Target = enemy;
            if (enemy.PlayerPawn != null && enemy.PlayerPawn.IsValid)
            {
                Entity.Leader.Raw = enemy.PlayerPawn.Raw;
            }
        }

        private void FollowLeader()
        {
            IsFollowingLeader = true;
            Target = null;
            var ownerPawn = Owner.PlayerPawn?.Value;
            if (ownerPawn != null && ownerPawn.IsValid && Owner.PlayerPawn != null && Owner.PlayerPawn.IsValid)
            {
                Entity.Leader.Raw = Owner.PlayerPawn.Raw;
            }
        }

        public void Dispose()
        {
            Kill();
        }
    }
}
