import argparse
import csv
import random
from datetime import datetime, timedelta

ACTIONS = [
    "Diseñar", "Revisar", "Implementar", "Optimizar", "Documentar",
    "Refactorizar", "Configurar", "Probar", "Desplegar", "Auditar"
]

COMPONENTS = [
    "Wireframes", "accesibilidad", "API de autenticación", "base de datos",
    "interfaz de usuario", "pipeline de CI/CD", "servicio de caché",
    "endpoints REST", "migración de esquemas", "pruebas unitarias"
]

DESCRIPTIONS = [
    "Bocetos de interfaz y flujos principales",
    "Validación de contraste, semántica y navegación por teclado",
    "Manejo de tokens JWT y renovación de sesiones",
    "Indexación de tablas críticas y análisis de consultas lentas",
    "Alineación con el sistema de diseño y componentes reusables",
    "Automatización de pruebas y despliegue a entorno de staging",
    "Invalidación de claves en Redis y reducción de latencia",
    "Estandarización de contratos de respuesta y códigos HTTP",
    "Ajustes de modelos para soportar nuevas entidades",
    "Cobertura mínima del 80% en lógica de negocio"
]

STATUSES = ["TODO", "IN_PROGRESS", "REVIEW", "DONE", "BLOCKED"]
PRIORITIES = ["LOW", "MEDIUM", "HIGH", "CRITICAL"]


def generate_mock_row():
    action = random.choice(ACTIONS)
    component = random.choice(COMPONENTS)
    title = f"{action} {component}"
    description = random.choice(DESCRIPTIONS)
    status = random.choice(STATUSES)
    priority = random.choice(PRIORITIES)

    # Genera una fecha entre hoy y los próximos 60 días
    days_ahead = random.randint(1, 60)
    due_date = (datetime.now() + timedelta(days=days_ahead)).strftime("%Y-%m-%d")

    return {
        "title": title,
        "description": description,
        "status": status,
        "priority": priority,
        "due_date": due_date,
    }


def main():
    parser = argparse.ArgumentParser(
        description="Generador de mock data para tareas en formato CSV."
    )
    parser.add_argument(
        "count",
        type=int,
        help="Cantidad de registros a generar"
    )
    parser.add_argument(
        "-o", "--output",
        default="tasks_mock.csv",
        help="Ruta o nombre del archivo de salida (por defecto: tasks_mock.csv)"
    )

    args = parser.parse_args()

    if args.count <= 0:
        raise ValueError("La cantidad de registros debe ser un número entero positivo.")

    fieldnames = ["title", "description", "status", "priority", "due_date"]

    with open(args.output, mode="w", newline="", encoding="utf-8") as csvfile:
        writer = csv.DictWriter(csvfile, fieldnames=fieldnames, quoting=csv.QUOTE_ALL)
        writer.writeheader()

        for _ in range(args.count):
            writer.writerow(generate_mock_row())

    print(f"Archivo generado exitosamente: {args.output} con {args.count} registros.")


if __name__ == "__main__":
    main()