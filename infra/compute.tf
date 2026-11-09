resource "aws_ecs_cluster" "application" {
  name = local.name

  setting {
    name  = "containerInsights"
    value = "disabled"
  }
}

resource "aws_ecs_cluster_capacity_providers" "application" {
  cluster_name       = aws_ecs_cluster.application.name
  capacity_providers = ["FARGATE", "FARGATE_SPOT"]
}

resource "aws_cloudwatch_log_group" "application" {
  name              = "/ecs/${local.name}"
  retention_in_days = var.log_retention_days
}

locals {
  common_environment = [
    { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
    { name = "ASPNETCORE_FORWARDEDHEADERS_ENABLED", value = "true" },
    { name = "AllowedHosts", value = "*" }
  ]

  application_environment = concat(local.common_environment, [
    { name = "Media__Provider", value = "S3" },
    { name = "Media__Bucket", value = aws_s3_bucket.media.id },
    { name = "Media__Region", value = var.region },
    { name = "Media__UseServerSideEncryption", value = "true" },
    { name = "Email__Provider", value = "SES" },
    { name = "Email__Region", value = var.region },
    { name = "Email__From", value = var.email_from },
    { name = "Email__PublicBaseUrl", value = local.application_url }
  ])

  log_configuration = {
    logDriver = "awslogs"
    options = {
      awslogs-group         = aws_cloudwatch_log_group.application.name
      awslogs-region        = var.region
      awslogs-stream-prefix = "guardacat"
    }
  }
}

resource "aws_ecs_task_definition" "web" {
  family                   = "${local.name}-web"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = tostring(var.task_cpu)
  memory                   = tostring(var.task_memory)
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([{
    name      = "guardacat"
    image     = local.container_image
    essential = true
    command   = ["--web"]

    portMappings = [{
      name          = "http"
      containerPort = 8080
      hostPort      = 8080
      protocol      = "tcp"
      appProtocol   = "http"
    }]

    environment = concat(local.application_environment, [
      { name = "Outbox__Enabled", value = "true" }
    ])

    secrets = [{
      name      = "ConnectionStrings__Postgres"
      valueFrom = aws_ssm_parameter.database_connection.arn
    }]

    logConfiguration = local.log_configuration
    linuxParameters  = { initProcessEnabled = true }
    stopTimeout      = 120
  }])
}

resource "aws_ecs_task_definition" "migration" {
  family                   = "${local.name}-migration"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = tostring(var.task_cpu)
  memory                   = tostring(var.task_memory)
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([{
    name      = "guardacat"
    image     = local.container_image
    essential = true
    command   = ["--migrate"]

    environment = concat(local.application_environment, [
      { name = "Outbox__Enabled", value = "false" }
    ])

    secrets = [{
      name      = "ConnectionStrings__Postgres"
      valueFrom = aws_ssm_parameter.database_connection.arn
    }]

    logConfiguration = local.log_configuration
    linuxParameters  = { initProcessEnabled = true }
  }])
}

resource "aws_ecs_service" "web" {
  name             = "${local.name}-web"
  cluster          = aws_ecs_cluster.application.id
  task_definition  = aws_ecs_task_definition.web.arn
  desired_count    = var.service_desired_count
  platform_version = "LATEST"

  capacity_provider_strategy {
    capacity_provider = "FARGATE"
    base              = 1
    weight            = 0
  }

  capacity_provider_strategy {
    capacity_provider = "FARGATE_SPOT"
    weight            = 1
  }

  deployment_minimum_healthy_percent = 100
  deployment_maximum_percent         = 200
  health_check_grace_period_seconds  = 60
  enable_ecs_managed_tags            = true
  propagate_tags                     = "SERVICE"

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  network_configuration {
    subnets          = aws_subnet.public[*].id
    security_groups  = [aws_security_group.tasks.id]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.application.arn
    container_name   = "guardacat"
    container_port   = 8080
  }

  depends_on = [
    aws_lb_listener.https,
    aws_ecs_cluster_capacity_providers.application,
    aws_ssm_parameter.database_connection
  ]
}
