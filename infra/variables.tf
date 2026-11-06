variable "project" {
  description = "Nombre corto usado en recursos y etiquetas."
  type        = string
  default     = "guardacat"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,19}$", var.project))
    error_message = "project debe tener entre 2 y 20 caracteres válidos."
  }
}

variable "environment" {
  description = "Entorno desplegado."
  type        = string
  default     = "prod"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,11}$", var.environment))
    error_message = "environment debe tener entre 2 y 12 caracteres válidos."
  }
}

variable "owner" {
  description = "Responsable que se aplicará como etiqueta Owner."
  type        = string

  validation {
    condition     = length(trimspace(var.owner)) > 0
    error_message = "owner no puede estar vacío."
  }
}

variable "region" {
  description = "Región AWS permitida por la cuenta de estudiante."
  type        = string
  default     = "eu-west-1"

  validation {
    condition     = var.region == "eu-west-1"
    error_message = "La cuenta del curso usa eu-west-1."
  }
}

variable "vpc_cidr" {
  type    = string
  default = "10.42.0.0/16"

  validation {
    condition     = can(cidrnetmask(var.vpc_cidr))
    error_message = "vpc_cidr debe ser un CIDR IPv4 válido."
  }
}

variable "app_domain" {
  description = "Subdominio de la aplicación administrado desde Hostinger."
  type        = string
  default     = "app.guarda.cat"
}

variable "ses_domain" {
  description = "Dominio que SES verificará mediante DKIM."
  type        = string
  default     = "guarda.cat"
}

variable "email_from" {
  description = "Remitente usado por la aplicación."
  type        = string
  default     = "no-reply@guarda.cat"

  validation {
    condition     = endswith(var.email_from, "@${var.ses_domain}")
    error_message = "email_from debe pertenecer a ses_domain."
  }
}

variable "ses_demo_recipient" {
  description = "Destinatario que SES verificará mientras la cuenta permanezca en sandbox."
  type        = string

  validation {
    condition     = can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.ses_demo_recipient))
    error_message = "ses_demo_recipient debe ser una dirección de correo válida."
  }
}

variable "image_tag" {
  description = "Etiqueta inmutable de la imagen, normalmente el SHA corto del commit."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$", var.image_tag))
    error_message = "image_tag no tiene un formato válido para ECR."
  }
}

variable "service_desired_count" {
  description = "Usar 0 hasta completar --migrate y 2 después."
  type        = number
  default     = 0

  validation {
    condition     = contains([0, 2], var.service_desired_count)
    error_message = "service_desired_count debe ser 0 durante la migración inicial o 2 para la demo."
  }
}

variable "task_cpu" {
  type    = number
  default = 512
}

variable "task_memory" {
  type    = number
  default = 1024
}

variable "db_name" {
  type    = string
  default = "guardacat"
}

variable "db_username" {
  type    = string
  default = "guardacat"
}

variable "db_instance_class" {
  type    = string
  default = "db.t4g.micro"
}

variable "db_allocated_storage" {
  type    = number
  default = 20
}

variable "db_max_allocated_storage" {
  type    = number
  default = 100
}

variable "db_backup_retention_days" {
  type    = number
  default = 7

  validation {
    condition     = var.db_backup_retention_days >= 7
    error_message = "RDS debe conservar al menos 7 días de backups."
  }
}

variable "db_deletion_protection" {
  type    = bool
  default = true
}

variable "db_skip_final_snapshot" {
  type    = bool
  default = false
}

variable "alb_deletion_protection" {
  type    = bool
  default = true
}

variable "log_retention_days" {
  type    = number
  default = 30
}
