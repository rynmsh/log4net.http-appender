namespace Log4Net.HttpAppender
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Web;
    using log4net.Appender;
    using log4net.Core;
    using System.Web.Script.Serialization;

    public class AsyncBatchHttpAppender : AppenderSkeleton
    {
        private readonly Queue<LoggingEvent> pendingTasks;
        private readonly object lockObject = new object();
        private readonly ManualResetEvent manualResetEvent;
        private bool onClosing;

        /// Configuration values (set in appender config)
        public string ServiceUrl { get; set; }
        public string ProjectKey { get; set; }
        public string Environment { get; set; }

        public AsyncBatchHttpAppender()
        {
            //initialize our queue
            pendingTasks = new Queue<LoggingEvent>();

            //put the event initially in non-signalled state
            manualResetEvent = new ManualResetEvent(false);

            //start the asyn process of handling pending tasks
            Start();
        }

        protected override void Append(LoggingEvent[] loggingEvents)
        {
            foreach (var loggingEvent in loggingEvents)
                Append(loggingEvent);
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (FilterEvent(loggingEvent))
                enqueue(loggingEvent);
        }

        private void Start()
        {
            // hopefully user doesnt open and close the GUI or CONSOLE OR WEBPAGE
            // right away. anyway lets add that condition too
            if (!onClosing)
            {
                var thread = new Thread(processMessageQueue);
                thread.Start();
            }
        }

        private void processMessageQueue()
        {
            // we keep on processing tasks until shutdown on repository is called
            while (!onClosing)
            {
                LoggingEvent loggingEvent;

                var batch = new List<LoggingEvent>();
                int count = 0, max = 20;

                while (deQueue(out loggingEvent))
                {
                    if (count == max) break;
                    batch.Add(loggingEvent);
                    count++;
                }

                if (batch.Any())
                {
                    Debug.WriteLine("++" + batch.Count + " EVENTS TO PUBLISH++");

                    try
                    {
                        sendBatch(batch);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("There was an error attempting to submit the logging events to endpoint");
                        Debug.Write(e.Message);
                        Debug.Write(e.StackTrace);
                    }

                    Debug.WriteLine("+++");
                }
                else
                    Debug.WriteLine("++0 EVENTS++");

                // if closing is already initiated break
                if (onClosing) break;

                // if they are no pending tasks sleep 10 seconds and try again
                Thread.Sleep(200);
                Debug.WriteLine("Sleeping for 100ms");
            }

            // we are done with our logging, sent the signal to the parent thread
            // so that it can commence shut down
            manualResetEvent.Set();
        }

        private void sendBatch(IEnumerable<LoggingEvent> batch)
        {
            using (var client = new WebClient())
            {
                var start = DateTime.Now;
                try
                {
                    var list = new List<dynamic>();

                    foreach (var le in batch)
                    {
                        var meta = new DropoffMeta { user = le.Identity, logger = le.LoggerName, time_stamp = le.TimeStamp };

                        if (null != le.ExceptionObject && !string.IsNullOrWhiteSpace(le.ExceptionObject.StackTrace))
                            meta.stack_trace = le.ExceptionObject.StackTrace;

                        if (le.Level.Name.ToLower() == "error")
                        {
                            if (null != HttpContext.Current)
                            {
                                var headers = new List<dynamic>();

                                foreach (var headerKey in HttpContext.Current.Request.Headers.AllKeys)
                                {
                                    var value = HttpContext.Current.Request.Headers[headerKey];
                                    if (!string.IsNullOrWhiteSpace(value))
                                        headers.Add(new { key = headerKey, value });
                                }

                                meta.http = new { request_headers = headers };
                            }
                        }

                        list.Add(new
                        {
                            level = le.Level.Name.ToLower(),
                            message = le.RenderedMessage,
                            meta
                        });
                    }

                    var json = new JavaScriptSerializer().Serialize(list);

                    // Add headers required to submit request and additional data
                    client.Headers.Add("X-ProjectKey", ProjectKey);
                    client.Headers.Add("X-Environment", Environment ?? System.Environment.MachineName);

                    // Set content type and encoding
                    client.Headers.Add("Content-Type", "application/json; charset=utf-8");
                    client.Encoding = System.Text.Encoding.UTF8;

                    // up the payload
                    client.UploadString(ServiceUrl, "POST", json);
                    Debug.WriteLine("Sent collection of events: " + list.Count);
                }
                catch (WebException ex)
                {
                    Debug.WriteLine("Error sending logging event to logmon dropoff {0}. Response data: {1}", ex.Message, ex.StackTrace);
                    throw;
                }
                finally
                {
                    Debug.WriteLine("Took " + ((DateTime.Now) - start).TotalMilliseconds + "ms to submit");
                }
            }
        }

        private void enqueue(LoggingEvent loggingEvent)
        {
            lock (lockObject)
            {
                pendingTasks.Enqueue(loggingEvent);
            }
        }

        private bool deQueue(out LoggingEvent loggingEvent)
        {
            lock (lockObject)
            {
                if (pendingTasks.Count > 0)
                {
                    loggingEvent = pendingTasks.Dequeue();
                    return true;
                }

                loggingEvent = null;
                return false;
            }
        }

        protected override void OnClose()
        {
            //set the OnClosing flag to true, so that
            //AppendLoggingEvents would know it is time to wrap up
            //whatever it is doing
            onClosing = true;

            //wait till we receive signal from manualResetEvent
            //which is signalled from AppendLoggingEvents
            manualResetEvent.WaitOne(TimeSpan.FromSeconds(5));
            //manualResetEvent.WaitOne();

            base.OnClose();
        }
    }

    public class DropoffMeta
    {
        public string user { get; set; }
        public string logger { get; set; }
        public DateTime time_stamp { get; set; }
        public dynamic http { get; set; }
        public string stack_trace { get; set; }
    }
}