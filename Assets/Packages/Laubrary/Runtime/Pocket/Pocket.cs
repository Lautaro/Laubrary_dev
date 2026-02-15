using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Laubrary.Pocket
{
    [Serializable]
    public class Pocket<T>
    {
        [SerializeField] private int amount;
        [SerializeField] private int stackSize;

        public int Amount => amount;
        public int StackSize => stackSize;
        public Pocket(int amount = 0, int stackSize = 100)
        {
            this.amount = amount;
            this.stackSize = stackSize;
        }

        /// <summary>
        /// Removes a specified number of items from the pocket.
        /// </summary>
        /// <param name="targetAmount">The desired number of items to remove.</param>
        /// <returns>The actual number of items removed from the pocket.</returns>
        /// <remarks>
        /// This method attempts to remove 'targetAmount' of items from the pocket. If 'targetAmount' is greater than the current amount in the pocket, only the available items are removed. The method returns the actual number of items removed, which may be less than 'targetAmount' if the pocket contains fewer items.
        /// </remarks>
        public Pocket<T> Remove(int targetAmount) {
            var remove = Mathf.Min(amount, targetAmount);
            amount -= remove;
            return new Pocket<T>(remove, stackSize);
        }

        /// <summary>
        /// Adds a specified number of items to the pocket up to its maximum stack size.
        /// </summary>
        /// <param name="targetAmount">The desired number of items to add.</param>
        /// <returns>A new Pocket<T> instance indicating the amount not added if adding the target amount would exceed the pocket's maxStackSize; otherwise, returns a Pocket<T> with zero items if the addition was fully successful.</returns>
        /// <remarks>
        /// This method attempts to add 'targetAmount' of items to the pocket. If adding 'targetAmount' would exceed the pocket's maxStackSize, only the number of items that fit are added, and a new Pocket<T> is returned with the difference, indicating the excess amount. If 'targetAmount' can be fully accommodated within the maxStackSize, the full amount is added, and a Pocket<T> with zero items is returned, indicating full success.
        /// </remarks>
        public Pocket<T> Add(int targetAmount)
        {
            var add = Mathf.Min(stackSize - amount, targetAmount);
            amount += add;
            return new Pocket<T>(targetAmount -add, stackSize);
        }


        /// <summary>
        /// Transfers a specified number of items from the source pocket to the current pocket.
        /// </summary>
        /// <param name="sourcePocket">The pocket from which items are to be transferred.</param>
        /// <param name="targetAmount">The desired number of items to transfer.</param>
        /// <returns>The actual number of items successfully transferred from the source pocket.</returns>
        /// <remarks>
        /// This method ensures a safe transfer of items between two pockets by first removing the specified amount from the source pocket. It then attempts to add the removed items to the current pocket. If the current pocket cannot accommodate all the removed items due to its maxStackSize limit, the excess items are returned to the source pocket. This process ensures that no items are lost during the transfer, maintaining the integrity of the inventory.
        /// </remarks>
        public int TransferTo(Pocket<T> sourcePocket, int targetAmount)
        {
            if (targetAmount < 0)// Reverse the transfer direction for negative values
            {   
                return sourcePocket.TransferTo(this, -targetAmount);
            }

            // Remove from sourcePocket
            var transferAmount = sourcePocket.Remove(targetAmount);
            
            // Add only what was actually taken from sourcePocket. Store the value that did not fit in target pocket.
            var addOverflow = Add(transferAmount.Amount);

            // Return the overflow to the source pocket
            sourcePocket.Add(addOverflow.Amount);

            return transferAmount.Amount;
        }
    }
}   