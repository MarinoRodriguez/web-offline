import argparse
import csv
import random

BASE_LIST_NAMES = [
    "Backlog",
    "Sprint Backlog",
    "Por Hacer",
    "En Curso",
    "En Revisión",
    "Control de Calidad",
    "Bloqueado",
    "Listo para Despliegue",
    "Desplegado a Staging",
    "Terminado",
    "Archivado",
    "Ideas / Propuestas",
    "Prioridad Alta",
    "En Espera de Cliente"
]

PALETTE = [
    "#3B82F6",  # Azul
    "#F59E0B",  # Ámbar / Amarillo
    "#10B981",  # Esmeralda / Verde
    "#EF4444",  # Rojo
    "#8B5CF6",  # Violeta
    "#EC4899",  # Rosa
    "#6366F1",  # Índigo
    "#14B8A6",  # Verde azulado (Teal)
    "#F97316",  # Naranja
    "#64748B",  # Gris pizarra
    "#06B6D4",  # Cian
    "#84CC16"   # Lima
]


def generate_color():
    return random.choice(PALETTE)


def get_list_name(index: int) -> str:
    if index < len(BASE_LIST_NAMES):
        return BASE_LIST_NAMES[index]
    # Si se piden más listas que nombres base disponibles:
    base = random.choice(BASE_LIST_NAMES)
    return f"{base} {index + 1}"


def main():
    parser = argparse.ArgumentParser(
        description="Generador de mock data para listas en formato CSV."
    )
    parser.add_argument(
        "count",
        type=int,
        help="Cantidad de registros de listas a generar"
    )
    parser.add_argument(
        "-o", "--output",
        default="lists_mock.csv",
        help="Ruta o nombre del archivo de salida (por defecto: lists_mock.csv)"
    )

    args = parser.parse_args()

    if args.count <= 0:
        raise ValueError("La cantidad de registros debe ser un número entero positivo.")

    fieldnames = ["name", "color", "position"]

    with open(args.output, mode="w", newline="", encoding="utf-8") as csvfile:
        # csv.QUOTE_NONNUMERIC mantiene comillas en strings y deja los enteros sin comillas
        writer = csv.DictWriter(
            csvfile,
            fieldnames=fieldnames,
            quoting=csv.QUOTE_NONNUMERIC
        )
        writer.writeheader()

        for i in range(args.count):
            position = i + 1
            name = get_list_name(i)
            color = generate_color()

            writer.writerow({
                "name": name,
                "color": color,
                "position": position
            })

    print(f"Archivo generado exitosamente: {args.output} con {args.count} listas.")


if __name__ == "__main__":
    main()