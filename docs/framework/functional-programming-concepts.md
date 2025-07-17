# Functional Programming
This document is not meant to introduce you to Functional programming. The [functional library](https://github.com/JYCabello/DeFuncto) used in this project has plenty of documentation to get started with that.

The goal here is to get you up and running with the types used in this project, by directly providing examples on where they are used.
## Discriminated Unions
This type is used in the `Auth` property of both the [commands](./command.md) and [read models](./read-model.md) definitions. Its signature is:
```cs
Du5<Everyone, EveryoneAuthenticated, PermissionRequired, AllPermissionsRequired, OnePermissionRequired>
```
The `Du5` name stands for `Discriminated Union of 5 types`, and it represents the possibility of its value being one and only one of the cases in the union. If all the types are different, it's able to implicitly cast one of the cases to the union type:
```cs
Du5<Everyone, EveryoneAuthenticated, PermissionRequired, AllPermissionsRequired, OnePermissionRequired> du5 =
    new PermissionRequired("some-role");
```
It's also used in the [todo task definition](./todo-task.md), where the task has as a return type:
```cs
Du<EventInsertion, TodoOutcome>
```

> Since the union is consumed by the framework, and not the user, this is enough information to make use of discriminated unions, for a deeper dive, head to the [library documentation](https://github.com/JYCabello/DeFuncto).
