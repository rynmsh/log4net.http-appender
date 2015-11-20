Log4Net Http Appender
===

Provides async logging to an http endpoint, log entries are queued and posted in a separate thread at the set 
batch max value, to prevent overly chunky requests. 

Thread sleeps after processing for 200ms then rechecks the queue. 

context = HttpContext.Current when context for error log level

Logging event sent in batched collection to service url

```
{ 
	user: loggingEvent.Identity, 
	logger: loggingEvent.LoggerName, 
	level: loggingEvent.Level.Name.ToLower(), 
	message: loggingEvent.RenderedMessage, 
	stack_trace: loggingEvent.ExceptionObject.StackTrace, 
	time_stamp: loggingEvent.TimeStamp.ToUniversalTime().ToString("u"), 
	http: {
		url: context.Request.Url,
        url_referrer: context.Request.UrlReferrer,
        user_agent: context.Request.UserAgent,
        user_host_address: context.Request.UserHostAddress,
        user_host_name: context.Request.UserHostName,
        http_method: context.Request.HttpMethod,
        current_user: string.IsNullOrWhiteSpace(context.User.Identity.Name) ? null : context.User.Identity.Name,
        authentication_type: string.IsNullOrWhiteSpace(context.User.Identity.AuthenticationType) ? null : context.User.Identity.AuthenticationType,
        request_headers (only not empty): {
			key: key,
			value: values
		}
	} 
} 
```

Configuration
===

```
<appender name="HttpAppender" type="Log4Net.HttpAppender.AsyncBatchHttpAppender, Log4Net.HttpAppender">
  <threshold value="Debug" />
  <ServiceUrl value="HTTP_ENDPOINT" />
  <ProjectKey value="FRIENDLY_DISPLAY_NAME" />
  <Environment value="dev|stage|prod" /> <!-- Optional: Default to machine name -->
  <BatchMaxSize value="20" /> <!-- Optional: Maximum number of events to submit per processing round. Default's to 40 -->
  <layout type="log4net.Layout.PatternLayout">
    <param name="ConversionPattern" value="%date [%identity] %-5level %logger - %message%newline" />
  </layout>
</appender>
```

/samples has example config for the appenders

NuGet
===

Package deployed to https://www.nuget.org/packages/Log4Net.HttpAppender/