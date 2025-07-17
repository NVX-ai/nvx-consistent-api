# Validation rules
Command endpoints can be covered with validation rules, which can be altered on runtime, and will be applied to the input data.

## Json logic
The validation rules can be defined in [json logic](https://jsonlogic.com/) format. Is out of the scope of the documentation to explain the syntax, the official website provides documentation, examples and a playground to try it out.

## Validation in the framework
Validation rules must be send to the endpoint `/commands/setvalidationrule` with the body conforming to the following schema:
```json
"SetValidationRule": {
  "required": [
    "commandName",
    "rule"
  ],
  "type": "object",
  "properties": {
    "commandName": {
      "type": "string"
    },
    "rule": {
      "type": "string"
    }
  },
  "additionalProperties": false
}
```

And this would be an example:
```json
{
  "commandName": "request-product",
  "rule": "{\"if\":[{\"and\":[{\"==\":[{\"var\":\"materialId\"},15]},{\">\":[{\"var\":\"amountRequested\"},5]}]},[\"cannot request more than 5 of material 15\"],[]]}"
}
```

The command name is the route segment for the command.

The `rule` is a json logic expression, which looks like this when unscaped and prettified:
```json
{
  "if": [
    { "and": [
        { "==": [ {"var": "materialId" }, 15 ] },
        { ">": [ {"var": "amountRequested" }, 5 ] }
      ]
    }, ["cannot request more than 5 of material 15"], []
  ]
}
```
This rule returns `["cannot request more than 5 of material 15"]` if the `materialId` is 15 and the `amountRequested` is greater than 5, and `[]` otherwise.

Errors are always returned as an array, being an empty array a success.

## Enabling validation for the command
Set the `UsesValidationRules` property to `true` in the command definition:
```cs
new CommandDefinition<RequestProduct, Product>
{
  Description = "Requests a product.",
  Auth = new RoleRequired("product-creator"),
  UsesValidationRules = true
}
```
