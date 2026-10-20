using HMS.Contracts.Common;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[ApiController]
[Produces("application/json")]
[ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
public abstract class ApiControllerBase : ControllerBase
{
    public const string V1 = "api/v1";
}
