// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking
{
    /// <summary>
    /// Summary description for UvWriteRequest
    /// </summary>
    public class UvShutdownRequest : UvRequest
    {
        private static readonly LibuvFunctions.uv_shutdown_cb _uv_shutdown_cb = (req, status) => UvShutdownCb(req, status);

        private Action<UvShutdownRequest, int, object> _callback;
        private object _state;

        public UvShutdownRequest(ILibuvTrace logger) : base(logger)
        {
        }

        public void Init(UvLoopHandle loop)
        {
            DangerousInit(loop);
        }

        public void DangerousInit(UvLoopHandle loop)
        {
            var requestSize = loop.Libuv.req_size(LibuvFunctions.RequestType.SHUTDOWN);
            CreateMemory(
                loop.Libuv,
                loop.ThreadId,
                requestSize);
        }

        public void Shutdown(
            UvStreamHandle handle,
            Action<UvShutdownRequest, int, object> callback,
            object state)
        {
            _callback = callback;
            _state = state;

            Libuv.shutdown(this, handle, _uv_shutdown_cb);
        }

        private static void UvShutdownCb(IntPtr ptr, int status)
        {
            var req = FromIntPtr<UvShutdownRequest>(ptr);

            var callback = req._callback;
            req._callback = null;

            var state = req._state;
            req._state = null;

            UvException error = null;
            if (status < 0)
            {
                req.Libuv.Check(status, out error);
            }

            try
            {
                callback(req, status, state);
            }
            catch (Exception ex)
            {
                req._log.LogError(0, ex, nameof(UvShutdownRequest));
                throw;
            }
        }
    }
}