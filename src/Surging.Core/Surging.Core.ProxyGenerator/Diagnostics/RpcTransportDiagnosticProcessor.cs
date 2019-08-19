﻿
using Surging.Core.CPlatform.Diagnostics;
using Surging.Core.CPlatform.Messages;
using Surging.Core.CPlatform.Serialization;
using Surging.Core.CPlatform.Utilities;
using System;
using System.Collections.Concurrent;
using System.Text;
using SurgingEvents = Surging.Core.CPlatform.Diagnostics.DiagnosticListenerExtensions;

namespace Surging.Core.ProxyGenerator.Diagnostics
{
    public class RpcTransportDiagnosticProcessor : ITracingDiagnosticProcessor
    {
        private Func<TransportEventData, string> _transportOperationNameResolver;
        public string ListenerName => SurgingEvents.DiagnosticListenerName;


        private readonly ConcurrentDictionary<string, SegmentContext> _resultDictionary =
            new ConcurrentDictionary<string, SegmentContext>();

        private readonly ISerializer<string> _serializer;
        private readonly ITracingContext _tracingContext;

        public Func<TransportEventData, string> TransportOperationNameResolver
        {
            get
            {
                return _transportOperationNameResolver ??
                       (_transportOperationNameResolver = (data) => "Rpc-Transport:: " + data.Message.MessageName);
            }
            set => _transportOperationNameResolver =
                value ?? throw new ArgumentNullException(nameof(TransportOperationNameResolver));
        }

        public RpcTransportDiagnosticProcessor(ITracingContext tracingContext, ISerializer<string> serializer)
        {
            _tracingContext = tracingContext;
            _serializer = serializer;
        }

        [DiagnosticName(SurgingEvents.SurgingBeforeTransport, TransportType.Rpc)]
        public void TransportBefore([Object] TransportEventData eventData)
        {
            var message = eventData.Message.GetContent<RemoteInvokeMessage>();
            var operationName = TransportOperationNameResolver(eventData);
            var context = _tracingContext.CreateEntrySegmentContext(operationName,
                new RpcTransportCarrierHeaderCollection(eventData.Headers));
            if (!string.IsNullOrEmpty(eventData.TraceId))
                context.TraceId = new UniqueId(BitConverter.ToInt64(Encoding.Default.GetBytes(eventData.TraceId), 0), 0, 0);
            context.Span.AddLog(LogEvent.Message($"Worker running at: {DateTime.Now}"));
            context.Span.SpanLayer = SpanLayer.RPC_FRAMEWORK;
            context.Span.Peer = new StringOrIntValue(eventData.RemoteAddress);
            _resultDictionary.TryAdd(eventData.OperationId.ToString(), context);
            context.Span.AddTag(Tags.RPC_METHOD, eventData.Method.ToString());
            context.Span.AddTag(Tags.RPC_PARAMETERS, _serializer.Serialize(message.Parameters));
            context.Span.AddTag(Tags.RPC_LOCAL_ADDRESS, NetUtils.GetHostAddress().ToString());
        }

        [DiagnosticName(SurgingEvents.SurgingAfterTransport, TransportType.Rpc)]
        public void TransportAfter([Object] ReceiveEventData eventData)
        {
            _resultDictionary.TryRemove(eventData.OperationId.ToString(), out SegmentContext context);
            if (context != null)
            {
                _tracingContext.Release(context);
            }
        }

        [DiagnosticName(SurgingEvents.SurgingErrorTransport, TransportType.Rpc)]
        public void TransportError([Object] TransportErrorEventData eventData)
        {
              _resultDictionary.TryRemove(eventData.OperationId.ToString(),out SegmentContext context);
            if (context != null)
            {
                context.Span.ErrorOccurred(eventData.Exception);
                _tracingContext.Release(context);
            }
        } 
    }
}