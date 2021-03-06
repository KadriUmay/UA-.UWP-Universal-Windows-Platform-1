/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// A base class for AsyncResult objects 
    /// </summary>
    public class AsyncResultBase : IAsyncResult, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncResultBase"/> class.
        /// </summary>
        /// <param name="callback">The callback to use when the operation completes.</param>
        /// <param name="callbackData">The callback data.</param>
        /// <param name="timeout">The timeout for the operation.</param>
        public AsyncResultBase(AsyncCallback callback, object callbackData, int timeout)
        {
            m_callback = callback;
            m_asyncState = callbackData;
            m_deadline = DateTime.MinValue;

            if (timeout > 0)
            {
                m_deadline = DateTime.UtcNow.AddMilliseconds(timeout);

                if (m_callback != null)
                {
                    m_timer = new Timer(OnTimeout, null, timeout, Timeout.Infinite);
                }
            }
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // stop the timer.
                if (m_timer != null)
                {
                    try
                    {
                        m_timer.Dispose();
                        m_timer = null;
                    }
                    catch (Exception)
                    {
                        // ignore 
                    }
                }

                // signal an waiting threads.
                if (m_waitHandle != null)
                {
                    try
                    {
                        m_waitHandle.Set();
                        m_waitHandle.Dispose();
                        m_waitHandle = null;
                    }
                    catch (Exception)
                    {
                        // ignore 
                    }
                }
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// An object used to synchronize access to the result object.
        /// </summary>
        public object Lock
        {
            get { return m_lock; }
        }

        /// <summary>
        /// An object used to synchronize access to the result object.
        /// </summary>
        public IAsyncResult InnerResult
        {
            get { return m_innerResult; }
            set { m_innerResult = value; }
        }

        /// <summary>
        /// An exception that occured during processing.
        /// </summary>
        public Exception Exception
        {
            get { return m_exception; }
            set { m_exception = value; }
        }

        /// <summary>
        /// Waits for the operation to complete.
        /// </summary>
        /// <param name="ar">The result object returned from the Begin method.</param>
        public static void WaitForComplete(IAsyncResult ar)
        {
            AsyncResultBase result = ar as AsyncResultBase;
            
            if (result == null)
            {
                throw new ArgumentException("IAsyncResult passed to call is not an instance of AsyncResultBase.");
            }

            if (!result.WaitForComplete())
            {
                throw new TimeoutException();
            }
        }

        /// <summary>
        /// Waits for the operation to complete.
        /// </summary>
        /// <returns>True if operation completed without any errors.</returns>
        public bool WaitForComplete()
        {
            try
            {
                WaitHandle waitHandle = null;

                int timeout = Timeout.Infinite;

                lock (m_lock)
                {
                    if (m_exception != null)
                    {
                        throw new ServiceResultException(m_exception, StatusCodes.BadCommunicationError);
                    }

                    if (m_deadline != DateTime.MinValue)
                    {
                        timeout = (int)(m_deadline - DateTime.UtcNow).TotalMilliseconds;

                        if (timeout <= 0)
                        {
                            return false;
                        }
                    }

                    if (m_isCompleted)
                    {
                        return true;
                    }

                    if (m_waitHandle == null)
                    {
                        m_waitHandle = new ManualResetEvent(false);
                    }

                    waitHandle = m_waitHandle;
                }

                if (waitHandle != null)
                {
                    try
                    {
                        if (!m_waitHandle.WaitOne(timeout))
                        {
                            return false;
                        }

                        lock (m_lock)
                        {
                            if (m_exception != null)
                            {
                                throw new ServiceResultException(m_exception, StatusCodes.BadCommunicationError);
                            }
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        return false;
                    }
                }
            }
            finally
            {
                // always stop the timer after operation completes.
                lock (m_lock)
                {
                    if (m_timer != null)
                    {
                        try
                        {
                            m_timer.Dispose();
                            m_timer = null;
                        }
                        catch (Exception)
                        {
                            // ignore 
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Called to invoke the callback after the asynchronous operation completes.
        /// </summary>
        public void OperationCompleted()
        {
            lock (m_lock)
            {
                m_isCompleted = true;

                // signal an waiting threads.
                if (m_waitHandle != null)
                {
                    try
                    {
                        m_waitHandle.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        // ignore 
                    }
                }
            }

            // invoke callback.
            if (m_callback != null)
            {
                m_callback(this);
            }
        }

        /// <summary>
        /// Called when the operation times out.
        /// </summary>
        private void OnTimeout(object state)
        {
            try
            {
                OperationCompleted();
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error handling timeout for ChannelAsyncResult operation.");
            }
        }
        #endregion

        #region IAsyncResult Members
        /// <summary>
        /// Gets a user-defined object that qualifies or contains information about an asynchronous operation.
        /// </summary>
        /// <returns>A user-defined object that qualifies or contains information about an asynchronous operation.</returns>
        public object AsyncState
        {
            get { return m_asyncState; }
            set { m_asyncState = value; }
        }

        /// <summary>
        /// Gets a <see cref="T:System.Threading.WaitHandle"/> that is used to wait for an asynchronous operation to complete.
        /// </summary>
        /// <returns>A <see cref="T:System.Threading.WaitHandle"/> that is used to wait for an asynchronous operation to complete.</returns>
        public WaitHandle AsyncWaitHandle
        {
            get
            {
                lock (m_lock)
                {
                    if (m_waitHandle == null)
                    {
                        m_waitHandle = new ManualResetEvent(false);
                    }

                    return m_waitHandle;
                }
            }
        }

        /// <summary>
        /// Gets a value that indicates whether the asynchronous operation completed synchronously.
        /// </summary>
        /// <returns>true if the asynchronous operation completed synchronously; otherwise, false.</returns>
        public bool CompletedSynchronously
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value that indicates whether the asynchronous operation has completed.
        /// </summary>
        /// <returns>true if the operation is complete; otherwise, false.</returns>
        public bool IsCompleted
        {
            get { return m_isCompleted; }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private AsyncCallback m_callback;
        private object m_asyncState;
        private ManualResetEvent m_waitHandle;
        private bool m_isCompleted;
        private IAsyncResult m_innerResult;
        private DateTime m_deadline;
        private Timer m_timer;
        private Exception m_exception;
        #endregion
    }
}
