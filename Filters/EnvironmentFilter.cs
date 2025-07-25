using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MultiSourceContentDelivery.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class EnvironmentFilterAttribute : Attribute, IResourceFilter
{
    private readonly string _environmentName;

    public EnvironmentFilterAttribute(string environmentName)
    {
        _environmentName = environmentName;
    }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var env = context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (!env.IsEnvironment(_environmentName))
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        // 不需要在执行后做任何事情
    }
}
