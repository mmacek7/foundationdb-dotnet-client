﻿#region BSD Licence
/* Copyright (c) 2013-2014, Doxense SAS
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
	* Redistributions of source code must retain the above copyright
	  notice, this list of conditions and the following disclaimer.
	* Redistributions in binary form must reproduce the above copyright
	  notice, this list of conditions and the following disclaimer in the
	  documentation and/or other materials provided with the distribution.
	* Neither the name of Doxense nor the
	  names of its contributors may be used to endorse or promote products
	  derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
#endregion

namespace FoundationDB.Layers.Counters
{
	using FoundationDB.Client;
	using FoundationDB.Layers.Tuples;
	using System;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// Represents an integer value which can be incremented without conflict.
	/// Uses a sharded representation (which scales with contention) along with background coalescing..
	/// </summary>
	[Obsolete("This is obsoleted by atomic operations")]
	public class FdbCounter
	{
		// from https://github.com/FoundationDB/python-layers/blob/master/lib/counter.py
		// NOTE: This is obsoleted for most practical purposes by the addition of atomic 
		// operations (transaction.add()) to FoundationDB 0.3.0, which do the same
		// thing more efficiently.

		// TODO: should we use a PRNG ? If two counter instances are created at the same moment, they could share the same seed ?
		private readonly Random Rng = new Random();

		/// <summary>Flag use to know if a background coalescing is already running</summary>
		private int m_coalesceRunning;

		private const int NOT_RUNNING = 0;
		private const int RUNNING = 1;

		/// <summary>
		/// Create a new object representing a binary large object (blob).
		/// Only keys within the subspace will be used by the object. 
		/// Other clients of the database should refrain from modifying the subspace.</summary>
		/// <param name="subspace">Subspace to be used for storing the blob data and metadata</param>
		public FdbCounter(IFdbDatabase db, FdbSubspace subspace)
		{
			if (db == null) throw new ArgumentNullException("db");
			if (subspace == null) throw new ArgumentNullException("subspace");

			this.Database = db;
			this.Subspace = subspace;
		}

		public FdbCounter(IFdbDatabase db, IFdbTuple tuple)
		{
			if (db == null) throw new ArgumentNullException("db");
			if (tuple == null) throw new ArgumentNullException("tuple");

			this.Database = db;
			this.Subspace = new FdbSubspace(tuple);
		}

		/// <summary>Subspace used as a prefix for all items in this table</summary>
		public FdbSubspace Subspace { get; private set; }

		/// <summary>Database instance that is used to perform background coalescing of the counter</summary>
		public IFdbDatabase Database { get; private set; }

		protected virtual Slice EncodeInt(long i)
		{
			return FdbTuple.Pack(i);
		}

		protected virtual long DecodeInt(Slice s)
		{
			return FdbTuple.UnpackSingle<long>(s);
		}

		protected virtual Slice RandomId()
		{
			lock (this.Rng) //note: the Rng is not thread-safe
			{
				return Slice.Random(this.Rng, 20);
			}
		}

		private async Task Coalesce(int N, CancellationToken ct)
		{
			long total = 0;

			using (var tr = this.Database.BeginTransaction(ct))
			{
				try
				{
					// read N writes from a random place in ID space
					var loc = this.Subspace.Pack(RandomId());

					bool right;
					lock(this.Rng) { right = this.Rng.NextDouble() < 0.5; }
					var query = right
						? tr.Snapshot.GetRange(loc, this.Subspace.ToRange().End, limit: N, reverse: false)
						: tr.Snapshot.GetRange(this.Subspace.ToRange().Begin, loc, limit: N, reverse: true);
					var shards = await query.ToListAsync().ConfigureAwait(false);

					if (shards.Count > 0)
					{
						// remove read shards transaction
						foreach (var shard in shards)
						{
							checked { total += DecodeInt(shard.Value); }
							await tr.GetAsync(shard.Key).ConfigureAwait(false); // real read for isolation
							tr.Clear(shard.Key);
						}

						tr.Set(this.Subspace.Pack(RandomId()), EncodeInt(total));

						// note: contrary to the python impl, we will await the commit, and rely on the caller to not wait to the Coalesce task itself to complete.
						// That way, the transaction will live as long as the task, and we ensure that it gets disposed at some time
						await tr.CommitAsync().ConfigureAwait(false);
					}
				}
				catch (FdbException x)
				{
					//TODO: logging ?
					System.Diagnostics.Debug.WriteLine("Coalesce error: " + x.Message);
					return;
				}
			}
		}

		private void BackgroundCoalesce(int n, CancellationToken ct)
		{
			// only coalesce if it is not already running
			if (Interlocked.CompareExchange(ref m_coalesceRunning, RUNNING, NOT_RUNNING) == NOT_RUNNING)
			{
				try
				{
					// fire and forget
					var _ = Task
						.Run(() => Coalesce(n, ct), ct)
						.ContinueWith((t) =>
						{
							// reset the flag
							Volatile.Write(ref m_coalesceRunning, NOT_RUNNING);

							// observe any exceptions
							if (t.IsFaulted)
							{
								var x = t.Exception;
								//TODO: logging ?
								System.Diagnostics.Debug.WriteLine("Background Coalesce error: " + x.ToString());
							}
						});
				}
				catch (Exception)
				{ // something went wrong starting the background coesle
					Volatile.Write(ref m_coalesceRunning, NOT_RUNNING);
					throw;
				}
			}
		}

		/// <summary>
		/// Get the value of the counter.
		/// Not recommended for use with read/write transactions when the counter is being frequently updated (conflicts will be very likely).
		/// </summary>
		public async Task<long> GetTransactional(IFdbReadOnlyTransaction trans)
		{
			if (trans == null) throw new ArgumentNullException("trans");

			long total = 0;
			await trans
				.GetRange(this.Subspace.ToRange())
				.ForEachAsync((kvp) => { checked { total += DecodeInt(kvp.Value); } })
				.ConfigureAwait(false);

			return total;
		}

		/// <summary>
		/// Get the value of the counter with snapshot isolation (no transaction conflicts).
		/// </summary>
		public Task<long> GetSnapshot(IFdbReadOnlyTransaction trans)
		{
			if (trans == null) throw new ArgumentNullException("trans");

			return GetTransactional(trans.Snapshot);
		}

		/// <summary>
		/// Add the value x to the counter.
		/// </summary>
		public void Add(IFdbTransaction trans, long x)
		{
			if (trans == null) throw new ArgumentNullException("trans");

			trans.Set(this.Subspace.Pack(RandomId()), EncodeInt(x));

			// decide if we must coalesce
			//note: Random() is not thread-safe so we must lock
			bool coalesce;
			lock (this.Rng) { coalesce = this.Rng.NextDouble() < 0.1; }
			if (coalesce)
			{
				//REVIEW: 20 is too small if there is a lot of activity on the counter !
				BackgroundCoalesce(20, CancellationToken.None);
			}
		}

		/// <summary>
		/// Set the counter to value x.
		/// </summary>
		public async Task SetTotal(IFdbTransaction trans, long x)
		{
			if (trans == null) throw new ArgumentNullException("trans");

			long value = await GetSnapshot(trans).ConfigureAwait(false);
			Add(trans, x - value);
		}

		#region Transactionals...

		/// <summary>
		/// Get the value of the counter.
		/// Not recommended for use with read/write transactions when the counter is being frequently updated (conflicts will be very likely).
		/// </summary>
		public Task<long> GetTransactionalAsync(CancellationToken cancellationToken)
		{
			return this.Database.ReadAsync((tr) => this.GetTransactional(tr), cancellationToken);
		}

		/// <summary>
		/// Get the value of the counter with snapshot isolation (no transaction conflicts).
		/// </summary>
		public Task<long> GetSnapshotAsync(CancellationToken cancellationToken)
		{
			return this.Database.ReadAsync((tr) => this.GetSnapshot(tr), cancellationToken);
		}

		/// <summary>
		/// Add the value x to the counter.
		/// </summary>
		public Task AddAsync(long x, CancellationToken cancellationToken)
		{
			return this.Database.WriteAsync((tr) => this.Add(tr, x), cancellationToken);
		}

		/// <summary>
		/// Set the counter to value x.
		/// </summary>
		public Task SetTotalAsync(long x, CancellationToken cancellationToken)
		{
			return this.Database.ReadWriteAsync((tr) => this.SetTotal(tr, x), cancellationToken);
		}

		#endregion

	}

}
