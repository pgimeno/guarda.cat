output "application_url" {
  value = local.application_url
}

output "hostinger_acm_validation_records" {
  description = "CNAME que debe copiarse en Hostinger antes del apply completo."
  value = [
    for option in aws_acm_certificate.application.domain_validation_options : {
      type  = option.resource_record_type
      name  = option.resource_record_name
      value = option.resource_record_value
    }
  ]
}

output "hostinger_ses_dkim_records" {
  description = "Tres CNAME DKIM que deben copiarse en Hostinger."
  value = [
    for token in aws_sesv2_email_identity.domain.dkim_signing_attributes[0].tokens : {
      type  = "CNAME"
      name  = "${token}._domainkey.${var.ses_domain}"
      value = "${token}.dkim.amazonses.com"
    }
  ]
}

output "hostinger_application_record" {
  description = "CNAME final de la aplicación que debe copiarse en Hostinger."
  value = {
    type  = "CNAME"
    name  = var.app_domain
    value = aws_lb.application.dns_name
  }
}

output "ecr_repository_url" {
  value = aws_ecr_repository.application.repository_url
}

output "ecs_cluster_name" {
  value = aws_ecs_cluster.application.name
}

output "ecs_service_name" {
  value = aws_ecs_service.web.name
}

output "migration_task_definition_arn" {
  value = aws_ecs_task_definition.migration.arn
}

output "migration_network" {
  value = {
    subnet_ids        = aws_subnet.public[*].id
    security_group_id = aws_security_group.tasks.id
    assign_public_ip  = true
  }
}

output "media_bucket" {
  value = aws_s3_bucket.media.id
}

output "ses_domain_status" {
  value = aws_sesv2_email_identity.domain.verification_status
}

output "ses_demo_recipient_status" {
  value = aws_sesv2_email_identity.demo_recipient.verification_status
}
